Option Explicit
Dim shell, script, powershell, command, exitCode, launchError
Set shell = CreateObject("WScript.Shell")
script = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName) & "\Install-Veyron.ps1"
powershell = shell.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
command = """" & powershell & """ -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File """ & script & """"
On Error Resume Next
exitCode = shell.Run(command, 0, True)
If Err.Number <> 0 Then
    launchError = Err.Description
    Err.Clear
    MsgBox "Не удалось запустить установщик Veyron v1." & vbCrLf & vbCrLf & launchError, vbCritical, "Veyron v1"
    WScript.Quit 1
End If
On Error GoTo 0
If exitCode <> 0 Then
    MsgBox "Установщик Veyron завершился с ошибкой (код " & exitCode & ")." & vbCrLf & vbCrLf & "Если появилось отдельное окно с причиной ошибки, следуйте его инструкции. Иначе скачайте архив заново: https://github.com/akaiboldiyev/Veyron/releases/latest", vbCritical, "Ошибка установки Veyron v1"
    WScript.Quit exitCode
End If
