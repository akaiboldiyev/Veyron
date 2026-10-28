Option Explicit
Dim shell, script, powershell, command
Set shell = CreateObject("WScript.Shell")
script = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName) & "\Uninstall-Veyron.ps1"
powershell = shell.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
command = """" & powershell & """ -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & script & """"
shell.Run command, 0, True
