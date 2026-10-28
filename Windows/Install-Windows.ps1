$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Install-Veyron.ps1'
if (-not (Test-Path -LiteralPath $installer)) { throw 'Build the installer first with Windows/Build-Windows.ps1.' }
& $installer
