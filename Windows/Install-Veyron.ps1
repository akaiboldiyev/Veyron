$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$packagePath = Join-Path $PSScriptRoot 'Veyron-v1.msix'
$certificatePath = Join-Path $PSScriptRoot 'Veyron-v1.cer'

if (-not (Test-Path -LiteralPath $packagePath)) {
    [System.Windows.Forms.MessageBox]::Show(
        "Не найден файл Veyron-v1.msix.`r`n`r`nСкорее всего, вы открыли этот скрипт прямо из ZIP-архива или скачали его отдельно.`r`n`r`nЧто сделать:`r`n1. Скачайте Veyron-v1-Installer.zip со страницы https://github.com/akaiboldiyev/Veyron/releases/latest`r`n2. В папке «Загрузки» нажмите на ZIP правой кнопкой → «Извлечь все…» → «Извлечь».`r`n3. Откройте созданную папку Veyron-v1-Installer и запустите Install-Veyron.vbs оттуда.`r`n`r`nФайлы Veyron-v1.msix и Veyron-v1.cer должны лежать рядом со скриптом.",
        'Как установить Veyron v1', 'OK', 'Warning'
    ) | Out-Null
    exit 1
}

if (-not (Test-Path -LiteralPath $certificatePath)) {
    [System.Windows.Forms.MessageBox]::Show(
        "Не найден сертификат Veyron-v1.cer.`r`n`r`nИзвлеките весь Veyron-v1-Installer.zip через правый щелчок → «Извлечь все…», затем запустите Install-Veyron.vbs из созданной папки.",
        'Не найден файл установщика', 'OK', 'Warning'
    ) | Out-Null
    exit 1
}

try {
    [System.Windows.Forms.MessageBox]::Show(
        "Для установки пакета Windows попросит доверить сертификат Veyron только вашей учётной записи. В системном окне издателем будет указан Veyron, а отпечаток должен начинаться с $((Get-PfxCertificate -FilePath $certificatePath).Thumbprint.Substring(0, 8)). Нажимайте «Да» только если запускаете установщик из официального релиза Veyron на GitHub.",
        'Подтверждение издателя Veyron', 'OK', 'Information'
    ) | Out-Null

    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
    Add-AppxPackage -Path $packagePath

    $installed = Get-AppxPackage -Name 'Veyron.Windows' | Select-Object -First 1
    if (-not $installed) { throw 'Windows did not report Veyron as installed.' }

    try { Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$($installed.PackageFamilyName)!Veyron" }
    catch { }

    [System.Windows.Forms.MessageBox]::Show(
        "Установка Veyron v1 завершена.`r`nПриложение добавлено в меню «Пуск» и запущено.`r`n`r`nVeyron запускается при входе в Windows. Чтобы отключить автозапуск, нажмите правой кнопкой на значок Veyron рядом с часами и снимите флажок «Запускать при входе в Windows». Пункт «Выход из Veyron» закрывает приложение до следующего запуска.",
        'Veyron v1 установлен', 'OK', 'Information'
    ) | Out-Null
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Не удалось установить Veyron v1.`r`n`r`n$($_.Exception.Message)`r`n`r`nПроверьте, что используется Windows 10 версии 1809 или новее, и повторите запуск Install-Veyron.vbs из распакованной папки.",
        'Ошибка установки Veyron v1', 'OK', 'Error'
    ) | Out-Null
    exit 1
}
