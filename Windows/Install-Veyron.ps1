$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$appSource = Join-Path $PSScriptRoot 'App'
$appExe = Join-Path $appSource 'Veyron.exe'
if (-not (Test-Path -LiteralPath $appExe)) {
    [System.Windows.Forms.MessageBox]::Show(
        "Не найдена папка App с Veyron.exe.`r`n`r`nСкачайте Veyron-v1-Installer.zip, затем в Проводнике нажмите ZIP правой кнопкой → «Извлечь все…» → «Извлечь». Запустите Install-Veyron.vbs из распакованной папки.",
        'Не найден файл приложения', 'OK', 'Warning'
    ) | Out-Null
    exit 1
}

try {
    $programs = Join-Path $env:LOCALAPPDATA 'Programs'
    $installDir = Join-Path $programs 'Veyron'
    $installExe = Join-Path $installDir 'Veyron.exe'
    $startMenu = [Environment]::GetFolderPath('Programs')
    $shortcutPath = Join-Path $startMenu 'Veyron v1.lnk'
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

    New-Item -ItemType Directory -Force -Path $programs | Out-Null
    if (Test-Path -LiteralPath $installExe) {
        Get-Process Veyron -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                if ($_.Path -and [IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($installExe)) { $_.CloseMainWindow() | Out-Null }
            } catch { }
        }
        Start-Sleep -Milliseconds 500
    }
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
    Copy-Item -Path (Join-Path $appSource '*') -Destination $installDir -Recurse -Force
    if (-not (Test-Path -LiteralPath $installExe)) { throw 'Файл Veyron.exe не скопирован в папку установки.' }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$installExe,0"
    $shortcut.Description = 'Veyron v1'
    $shortcut.Save()
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name 'Veyron' -Value ('"' + $installExe + '"')

    Start-Process -FilePath $installExe -WorkingDirectory $installDir
    [System.Windows.Forms.MessageBox]::Show(
        "Veyron v1 установлен и запущен.`r`n`r`nОткрыть его позже можно через меню «Пуск» → «Veyron v1». Приложение запускается при входе в Windows. Чтобы отключить автозапуск, используйте флажок в меню значка Veyron рядом с часами.`r`n`r`nУстановщик не добавляет сертификаты и не меняет системные настройки безопасности.",
        'Veyron v1 установлен', 'OK', 'Information'
    ) | Out-Null
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Не удалось установить Veyron v1.`r`n`r`n$($_.Exception.Message)",
        'Ошибка установки Veyron v1', 'OK', 'Error'
    ) | Out-Null
    exit 1
}
