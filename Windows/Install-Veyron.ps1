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
        $installExeFullPath = [IO.Path]::GetFullPath($installExe)
        $runningInstances = @(Get-Process Veyron -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and [IO.Path]::GetFullPath($_.Path) -eq $installExeFullPath } catch { $false }
        })
        foreach ($process in $runningInstances) {
            try { $null = $process.CloseMainWindow() } catch { }
        }

        # Veyron normally lives in the notification area, so CloseMainWindow may
        # not close it. Wait briefly for a normal exit, then stop only instances
        # launched from this install directory so their files can be replaced.
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
            if (-not $process.WaitForExit(5000)) { throw 'Не удалось закрыть Veyron перед обновлением. Закройте приложение в области уведомлений и запустите установщик ещё раз.' }
        }
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
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Не удалось установить Veyron v1.`r`n`r`n$($_.Exception.Message)",
        'Ошибка установки Veyron v1', 'OK', 'Error'
    ) | Out-Null
    exit 1
}
