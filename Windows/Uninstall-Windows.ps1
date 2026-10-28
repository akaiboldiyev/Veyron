$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Uninstall-Veyron.ps1'
if (-not (Test-Path -LiteralPath $installer)) { throw 'Uninstall-Veyron.ps1 was not found.' }
& $installer
