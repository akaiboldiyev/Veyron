$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

try {
    $installDir = Join-Path $env:LOCALAPPDATA 'Programs\Veyron'
    $installExe = Join-Path $installDir 'Veyron.exe'
    $startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Veyron v1.lnk'
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

    $installExeFullPath = [IO.Path]::GetFullPath($installExe)
    $runningInstances = @(Get-Process Veyron -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and [IO.Path]::GetFullPath($_.Path) -eq $installExeFullPath } catch { $false }
    })
    foreach ($process in $runningInstances) {
        try { $null = $process.CloseMainWindow() } catch { }
    }

    # Veyron can be minimized to the notification area, where CloseMainWindow
    # does not close it. Stop only copies from this install directory so files
    # are unlocked before the app is removed.
    $closeDeadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $runningInstances | ForEach-Object { try { $_.Refresh() } catch { } }
        $runningInstances = @($runningInstances | Where-Object { -not $_.HasExited })
        if ($runningInstances.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $closeDeadline)

    foreach ($process in $runningInstances) {
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
    }
    foreach ($process in $runningInstances) {
        if (-not $process.WaitForExit(5000)) { throw 'Не удалось закрыть Veyron. Повторите удаление после закрытия приложения в области уведомлений.' }
    }

    if (Test-Path -LiteralPath $runKey) { Remove-ItemProperty -Path $runKey -Name 'Veyron' -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $startMenuShortcut) { Remove-Item -LiteralPath $startMenuShortcut -Force }
    if (Test-Path -LiteralPath $installDir) { Remove-Item -LiteralPath $installDir -Recurse -Force }

}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Не удалось удалить Veyron.`r`n`r`n$($_.Exception.Message)",
        'Ошибка удаления Veyron v1', 'OK', 'Error'
    ) | Out-Null
    exit 1
}
