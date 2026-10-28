$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

try {
    $installDir = Join-Path $env:LOCALAPPDATA 'Programs\Veyron'
    $installExe = Join-Path $installDir 'Veyron.exe'
    $startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Veyron v1.lnk'
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

    if (Test-Path -LiteralPath $runKey) { Remove-ItemProperty -Path $runKey -Name 'Veyron' -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $startMenuShortcut) { Remove-Item -LiteralPath $startMenuShortcut -Force }
    Get-Process Veyron -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and [IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($installExe)) { $_.CloseMainWindow() | Out-Null }
        } catch { }
    }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $installDir) { Remove-Item -LiteralPath $installDir -Recurse -Force }

    [System.Windows.Forms.MessageBox]::Show(
        "Veyron v1 удалён. Настройки и сохранённые файлы в %LOCALAPPDATA%\Cyclop оставлены без изменений.",
        'Veyron v1', 'OK', 'Information'
    ) | Out-Null
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Не удалось удалить Veyron.`r`n`r`n$($_.Exception.Message)",
        'Ошибка удаления Veyron v1', 'OK', 'Error'
    ) | Out-Null
    exit 1
}
