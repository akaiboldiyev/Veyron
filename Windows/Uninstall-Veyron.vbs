Option Explicit
Dim shell, script, powershell, command, exitCode, launchError
Set shell = CreateObject("WScript.Shell")
script = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName) & "\Uninstall-Veyron.ps1"
powershell = shell.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
command = """" & powershell & """ -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File """ & script & """"
On Error Resume Next
exitCode = shell.Run(command, 0, True)
If Err.Number <> 0 Then
    launchError = Err.Description
    Err.Clear
    MsgBox "Не удалось запустить удаление Veyron." & vbCrLf & vbCrLf & launchError, vbCritical, "Veyron v1"
    WScript.Quit 1
End If
On Error GoTo 0
If exitCode <> 0 Then
    MsgBox "Удаление Veyron завершилось с ошибкой (код " & exitCode & "). Подробности показаны в предыдущем окне.", vbCritical, "Veyron v1"
    WScript.Quit exitCode
End If
