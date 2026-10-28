using System.Diagnostics;
using System.Text.Json;

namespace Cyclop.Windows;

internal sealed class TranslationService
{
    private readonly string venv=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Cyclop","translation","venv");
    private string Python=>Path.Combine(venv,"Scripts","python.exe");
    private string ReadyMarker=>Path.Combine(venv,"cyclop-ready.txt");
    private const string ReadyValue="ru-en,en-ru";
    private bool installing;
    private readonly SemaphoreSlim workerGate=new(1,1);
    private Process? worker;
    private Task<string>? workerErrors;
    private static readonly JsonSerializerOptions WorkerJson=new(JsonSerializerDefaults.Web);
    public static TranslationService Current{get;}=new();
    public bool IsReady
    {
        get{try{return File.Exists(Python)&&File.Exists(ReadyMarker)&&string.Equals(File.ReadAllText(ReadyMarker).Trim(),ReadyValue,StringComparison.Ordinal);}catch{return false;}}
    }
    public event Action<string>? StatusChanged;

    public async Task<bool> InstallAsync()
    {
        if(installing){StatusChanged?.Invoke("Установка переводчика уже выполняется.");return false;}
        installing=true;
        try
        {
            if(IsReady&&await VerifyModelsAsync()){StatusChanged?.Invoke("Офлайн‑переводчик уже установлен.");return true;}
            if(File.Exists(ReadyMarker))File.Delete(ReadyMarker);
            var systemPython=await FindPythonAsync();
            if(systemPython is null)
            {
                StatusChanged?.Invoke("Устанавливаю Python для офлайн‑переводчика…");
                var winget=await RunAsync("winget.exe",["install","--id","Python.Python.3.12","--exact","--silent","--accept-source-agreements","--accept-package-agreements"]);
                if(winget.ExitCode!=0)return Fail("Не удалось установить Python. Установите Python 3.12 и повторите.");
                systemPython=await FindPythonAsync();if(systemPython is null)return Fail("Python не найден после установки.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(venv)!);
            StatusChanged?.Invoke("Создаю локальное окружение…");
            var venvResult=await RunAsync(systemPython,["-m","venv","--clear",venv]);if(venvResult.ExitCode!=0)return Fail(venvResult.Output);
            if(File.Exists(ReadyMarker))File.Delete(ReadyMarker);
            StatusChanged?.Invoke("Устанавливаю локальный движок перевода…");
            var pip=await RunAsync(Python,["-m","pip","install","--upgrade","pip","argostranslate"]);if(pip.ExitCode!=0)return Fail(pip.Output);
            StatusChanged?.Invoke("Загружаю русско‑английские языковые модели…");
            var installModels="""
import sys
from argostranslate import package
package.update_package_index()
installed=package.get_installed_packages()
for src,dst in [('ru','en'),('en','ru')]:
    if not any(p.from_code==src and p.to_code==dst for p in installed):
        matches=[p for p in package.get_available_packages() if p.from_code==src and p.to_code==dst]
        if not matches: raise RuntimeError(f'Missing model {src}->{dst}')
        item=matches[-1]
        path=item.download()
        package.install_from_path(path)
print('OK')
""";
            var models=await RunWithInputAsync(Python,["-c",installModels],"");if(models.ExitCode!=0)return Fail(models.Output);
            if(!await VerifyModelsAsync())return Fail("Обе языковые модели не прошли проверку после установки.");
            var markerTemp=ReadyMarker+".tmp";File.WriteAllText(markerTemp,ReadyValue);File.Move(markerTemp,ReadyMarker,true);
            StatusChanged?.Invoke("Офлайн‑переводчик готов.");return true;
        }
        catch(Exception ex){return Fail(ex.Message);}
        finally{installing=false;}
    }

    public async Task<string> TranslateAsync(string text,string from,string to)
    {
        if(!IsReady)
        {
            throw new InvalidOperationException("Офлайн‑движок или языковая модель отсутствует. Нажмите «Установить офлайн‑переводчик» для восстановления.");
        }
        await workerGate.WaitAsync();
        try
        {
            var active=await EnsureWorkerAsync();
            var request=JsonSerializer.Serialize(new WorkerRequest(text,from,to),WorkerJson);
            await active.StandardInput.WriteLineAsync(request);
            await active.StandardInput.FlushAsync();
            var responseLine=await active.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(2));
            if(responseLine is null)
            {
                var error=workerErrors is null?"":await workerErrors;
                StopWorker();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)?"Локальный движок перевода завершил работу.":error.Trim());
            }
            var response=JsonSerializer.Deserialize<WorkerResponse>(responseLine.TrimStart('\uFEFF'),WorkerJson)
                ??throw new InvalidOperationException("Локальный движок вернул пустой ответ.");
            if(!string.IsNullOrWhiteSpace(response.Error))
            {
                if(response.Error.Contains("CYCLOP_MISSING_MODELS",StringComparison.Ordinal)&&File.Exists(ReadyMarker))File.Delete(ReadyMarker);
                throw new InvalidOperationException(response.Error);
            }
            return response.Result??"";
        }
        catch
        {
            StopWorker();
            throw;
        }
        finally{workerGate.Release();}
    }

    public async Task WarmAsync()
    {
        if(!IsReady)return;
        await workerGate.WaitAsync();
        try
        {
            if(worker is {HasExited:false})return;
            StatusChanged?.Invoke("Подготавливаю офлайн‑переводчик…");
            await EnsureWorkerAsync();
            StatusChanged?.Invoke("Офлайн‑переводчик готов.");
        }
        catch(Exception ex){StatusChanged?.Invoke("Ошибка запуска переводчика: "+ex.Message);}
        finally{workerGate.Release();}
    }

    public void StopWorker()
    {
        var active=worker;worker=null;workerErrors=null;
        if(active is null)return;
        try
        {
            if(!active.HasExited)
            {
                active.StandardInput.Close();
                if(!active.WaitForExit(1500))active.Kill(true);
            }
        }
        catch{try{if(!active.HasExited)active.Kill(true);}catch{}}
        finally{active.Dispose();}
    }

    private async Task<Process> EnsureWorkerAsync()
    {
        if(worker is {HasExited:false})return worker;
        StopWorker();
        var script="""
import json, sys
from argostranslate import package, translate
installed={(p.from_code,p.to_code) for p in package.get_installed_packages()}
required={('ru','en'),('en','ru')}
missing=required-installed
if missing:
    print(json.dumps({'error':'CYCLOP_MISSING_MODELS '+repr(sorted(missing))}),flush=True)
    sys.exit(23)
translate.translate('Cyclop','en','ru')
translate.translate('Привет','ru','en')
print(json.dumps({'ready':True}),flush=True)
for line in sys.stdin:
    try:
        request=json.loads(line)
        result=translate.translate(request['text'],request['source'],request['target'])
        response={'result':result}
    except Exception as error:
        response={'error':str(error)}
    print(json.dumps(response,ensure_ascii=False),flush=True)
""";
        var start=new ProcessStartInfo(Python)
        {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,
            StandardInputEncoding=new System.Text.UTF8Encoding(false),StandardOutputEncoding=new System.Text.UTF8Encoding(false)
        };
        start.Environment["PYTHONUTF8"]="1";start.Environment["PYTHONIOENCODING"]="utf-8";
        start.ArgumentList.Add("-u");start.ArgumentList.Add("-c");start.ArgumentList.Add(script);
        var process=Process.Start(start)??throw new InvalidOperationException("Не удалось запустить локальный движок перевода.");
        var errors=process.StandardError.ReadToEndAsync();
        worker=process;workerErrors=errors;
        var hello=await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(2));
        var ready=hello is null?null:JsonSerializer.Deserialize<WorkerResponse>(hello.TrimStart('\uFEFF'),WorkerJson);
        if(ready?.Ready!=true)
        {
            var diagnostic=ready?.Error??(await errors).Trim();
            if(diagnostic.Contains("CYCLOP_MISSING_MODELS",StringComparison.Ordinal)&&File.Exists(ReadyMarker))File.Delete(ReadyMarker);
            StopWorker();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(diagnostic)?"Локальный движок перевода не запустился.":diagnostic);
        }
        return process;
    }

    private async Task<bool> VerifyModelsAsync()
    {
        if(!File.Exists(Python))return false;
        const string script="""
from argostranslate import package
installed={(p.from_code,p.to_code) for p in package.get_installed_packages()}
required={('ru','en'),('en','ru')}
missing=required-installed
if missing: raise RuntimeError('Missing translation models: '+repr(sorted(missing)))
print('OK')
""";
        try
        {
            var result=await RunAsync(Python,["-c",script]);
            return result.ExitCode==0&&result.Output.Contains("OK",StringComparison.Ordinal);
        }
        catch{return false;}
    }

    private bool Fail(string message){StatusChanged?.Invoke("Ошибка установки: "+message.Trim());return false;}
    private static async Task<string?> FindPythonAsync()
    {
        // Argos' native dependencies are most consistently available for
        // Python 3.12 on Windows; prefer that runtime over a newer system
        // Python so an unrelated default installation cannot break setup.
        foreach(var version in new[]{"Python312","Python311","Python310"})
        {
            var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Python",version,"python.exe");
            if(File.Exists(path))return path;
        }
        foreach(var launcher in new[]{"py.exe"})
        {
            try
            {
                foreach(var version in new[]{"3.12","3.11","3.10"})
                {
                    var result=await RunAsync(launcher,["-"+version,"-c","import sys;print(sys.executable)"]);
                    var path=result.Output.Trim().Split('\n').LastOrDefault()?.Trim();
                    if(result.ExitCode==0&&path is not null&&File.Exists(path))return path;
                }
            }catch{ }
        }
        foreach(var executable in new[]{"python.exe","python3.exe"})
        {
            try
            {
                var result=await RunAsync(executable,["-c","import sys;print(sys.version_info[0],sys.version_info[1],sys.executable)"]);
                var fields=result.Output.Trim().Split(' ',3,StringSplitOptions.RemoveEmptyEntries);
                if(result.ExitCode==0&&fields.Length==3&&int.TryParse(fields[0],out var major)&&int.TryParse(fields[1],out var minor)&&major==3&&minor is >=10 and <=12&&File.Exists(fields[2]))return fields[2];
            }catch{ }
        }
        return null;
    }
    private static Task<ProcessResult> RunAsync(string executable,IReadOnlyList<string> args)=>RunWithInputAsync(executable,args,null);
    private static async Task<ProcessResult> RunWithInputAsync(string executable,IReadOnlyList<string> args,string? input)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=input is not null};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new InvalidOperationException($"Не удалось запустить {executable}");
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        if(input is not null){await process.StandardInput.WriteAsync(input);process.StandardInput.Close();}
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode,await stdout,await stderr);
    }
    private sealed record ProcessResult(int ExitCode,string StandardOutput,string StandardError)
    {public string Output=>StandardOutput+Environment.NewLine+StandardError;}
    private sealed record WorkerRequest(string Text,string Source,string Target);
    private sealed record WorkerResponse(bool Ready=false,string? Result=null,string? Error=null);
}
