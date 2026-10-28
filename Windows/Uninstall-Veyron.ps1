$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

try {
    $installed = Get-AppxPackage -Name 'Veyron.Windows' | Select-Object -First 1
    if ($installed) { Remove-AppxPackage -Package $installed.PackageFullName }

    $certificatePath = Join-Path $PSScriptRoot 'Veyron-v1.cer'
    if (Test-Path -LiteralPath $certificatePath) {
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
        Get-ChildItem Cert:\CurrentUser\TrustedPeople |
            Where-Object Thumbprint -eq $certificate.Thumbprint |
            Remove-Item -Force
        $certificate.Dispose()
    }

    [System.Windows.Forms.MessageBox]::Show(
        "Veyron v1 has been removed. Your saved files remain in %LOCALAPPDATA%\Cyclop.",
        'Veyron v1', 'OK', 'Information'
    ) | Out-Null
}
catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Veyron could not be removed.`r`n`r`n$($_.Exception.Message)",
        'Veyron v1 — uninstall error', 'OK', 'Error'
    ) | Out-Null
    exit 1
}
