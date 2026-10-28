$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$packagePath = Join-Path $PSScriptRoot 'Veyron-v1.msix'
$certificatePath = Join-Path $PSScriptRoot 'Veyron-v1.cer'

try {
    if (-not (Test-Path -LiteralPath $packagePath)) { throw 'Veyron-v1.msix is missing. Extract the entire installer ZIP first.' }
    if (-not (Test-Path -LiteralPath $certificatePath)) { throw 'Veyron-v1.cer is missing. Extract the entire installer ZIP first.' }

    Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
    Add-AppxPackage -Path $packagePath

    $installed = Get-AppxPackage -Name 'Veyron.Windows' | Select-Object -First 1
    if (-not $installed) { throw 'Windows did not report Veyron as installed.' }

    try { Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$($installed.PackageFamilyName)!Veyron" }
    catch { }

    [System.Windows.Forms.MessageBox]::Show(
        "Veyron v1 is installed and has been opened.`r`n`r`nIt starts when you sign in to Windows. To turn that off, right-click the Veyron tray icon and clear “Запускать при входе в Windows”. Choose “Выход из Veyron” to close it for this session.",
        'Veyron v1', 'OK', 'Information'
    ) | Out-Null
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Veyron could not be installed.`r`n`r`n$($_.Exception.Message)",
        'Veyron v1 — installation error', 'OK', 'Error'
    ) | Out-Null
    exit 1
}
