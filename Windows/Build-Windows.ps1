$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'Cyclop.Windows\Cyclop.Windows.csproj'
$testsProject = Join-Path $PSScriptRoot 'Cyclop.Windows.Tests\Cyclop.Windows.Tests.csproj'
$nugetConfig = Join-Path $PSScriptRoot 'NuGet.Config'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$bundledDotnet = Join-Path $root 'build\Windows\.dotnet-sdk\dotnet.exe'
$dotnet = if ($dotnetCommand) { $dotnetCommand.Source } elseif (Test-Path -LiteralPath $bundledDotnet) { $bundledDotnet } else { $null }
if (-not $dotnet) { throw '.NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0.' }
$buildDir = Join-Path $root 'build\Windows'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $buildDir '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $buildDir '.nuget'
# Keep restores deterministic. A broken or inaccessible user-wide NuGet
# configuration must not prevent Cyclop from producing its Windows build.
$env:APPDATA = Join-Path $buildDir '.appdata'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES,$env:APPDATA | Out-Null
$publishDir = Join-Path $buildDir 'publish'

function Remove-BuildDirectory([string]$path) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    $fullBuildDir = [System.IO.Path]::GetFullPath($buildDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($fullBuildDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the build directory: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) { Remove-Item -LiteralPath $fullPath -Recurse -Force }
}

try {
    & $dotnet restore $project --configfile $nugetConfig -r win-x64
    if ($LASTEXITCODE -ne 0) { throw "Cyclop restore failed with exit code $LASTEXITCODE." }
    & $dotnet restore $testsProject --configfile $nugetConfig -r win-x64
    if ($LASTEXITCODE -ne 0) { throw "Cyclop tests restore failed with exit code $LASTEXITCODE." }
    & $dotnet run --project $testsProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Cyclop tests failed with exit code $LASTEXITCODE." }

    Remove-BuildDirectory $publishDir
    & $dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Cyclop publish failed with exit code $LASTEXITCODE." }

    $installerFiles = @(
        'Install-Veyron.ps1', 'Install-Veyron.vbs',
        'Uninstall-Veyron.ps1', 'Uninstall-Veyron.vbs', 'README-Install-Veyron.txt'
    )
    foreach ($file in $installerFiles) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $buildDir $file) -Force
    }
    $installerDir = Join-Path $buildDir 'Veyron-v1-Installer'
    Remove-BuildDirectory $installerDir
    New-Item -ItemType Directory -Path $installerDir | Out-Null
    $appDir = Join-Path $installerDir 'App'
    New-Item -ItemType Directory -Path $appDir | Out-Null
    Copy-Item -Path (Join-Path $publishDir '*') -Destination $appDir -Recurse -Force
    foreach ($file in $installerFiles) {
        Copy-Item -LiteralPath (Join-Path $buildDir $file) -Destination (Join-Path $installerDir $file) -Force
    }
    if (-not (Test-Path -LiteralPath (Join-Path $appDir 'Veyron.exe'))) { throw 'Published Veyron.exe is missing from installer payload.' }
    $installerZip = Join-Path $buildDir 'Veyron-v1-Installer.zip'
    if (Test-Path -LiteralPath $installerZip) { Remove-Item -LiteralPath $installerZip -Force }
    Compress-Archive -Path (Join-Path $installerDir '*') -DestinationPath $installerZip -CompressionLevel Optimal

    Write-Host "Portable app output: $publishDir"
    Write-Host "Installer output: $installerZip"
    Write-Host 'Extract the ZIP, then double-click Install-Veyron.vbs. No package signing or certificate is used.'
}
catch {
    throw
}
