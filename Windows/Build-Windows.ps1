$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'Cyclop.Windows\Cyclop.Windows.csproj'
$testsProject = Join-Path $PSScriptRoot 'Cyclop.Windows.Tests\Cyclop.Windows.Tests.csproj'
$toolsProject = Join-Path $PSScriptRoot 'BuildTools\Cyclop.BuildTools.csproj'
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
# configuration must not prevent Cyclop from producing its own package.
$env:APPDATA = Join-Path $buildDir '.appdata'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME,$env:NUGET_PACKAGES,$env:APPDATA | Out-Null
$publishDir = Join-Path $buildDir 'publish'
$stageDir = Join-Path $buildDir 'msix-stage'
$verifyDir = Join-Path $buildDir 'verify-stage'
$packagePath = Join-Path $buildDir 'Veyron-v1.msix'
$nugetPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$packageTools = Join-Path $nugetPackages 'microsoft.windows.sdk.buildtools\10.0.28000.2705\bin\10.0.28000.0\x64'
$makeAppx = Join-Path $packageTools 'makeappx.exe'
$signTool = Join-Path $packageTools 'signtool.exe'
$certPath = Join-Path $buildDir ('Veyron-dev-' + [guid]::NewGuid().ToString('N') + '.pfx')
$certPathFile = Join-Path $buildDir 'Veyron-v1.cer'
$password = [guid]::NewGuid().ToString('N')

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
    & $dotnet restore $toolsProject --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) { throw "Windows SDK Build Tools restore failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath $makeAppx) -or -not (Test-Path -LiteralPath $signTool)) {
        throw "MakeAppx or SignTool was not found under $packageTools."
    }

    & $dotnet run --project $testsProject -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Cyclop tests failed with exit code $LASTEXITCODE." }

    Remove-BuildDirectory $publishDir
    & $dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Cyclop publish failed with exit code $LASTEXITCODE." }

    Remove-BuildDirectory $stageDir
    New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
    Copy-Item -Path (Join-Path $publishDir '*') -Destination $stageDir -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Cyclop.Windows\Package.appxmanifest') -Destination (Join-Path $stageDir 'AppxManifest.xml') -Force
    Copy-Item -Path (Join-Path $PSScriptRoot 'Cyclop.Windows\Assets') -Destination $stageDir -Recurse -Force
    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
    & $makeAppx pack /v /o /h SHA256 /d $stageDir /p $packagePath
    if ($LASTEXITCODE -ne 0) { throw "MSIX packaging failed with exit code $LASTEXITCODE." }

    $key = [System.Security.Cryptography.RSA]::Create(2048)
    $subject = [System.Security.Cryptography.X509Certificates.X500DistinguishedName]::new('CN=Veyron')
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        $subject, $key, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    )
    $usage = [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $false
    )
    $request.CertificateExtensions.Add($usage)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(3))
    [System.IO.File]::WriteAllBytes($certPath, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
    [System.IO.File]::WriteAllBytes($certPathFile, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    $certificate.Dispose()
    $key.Dispose()
    & $signTool sign /fd SHA256 /f $certPath /p $password $packagePath
    if ($LASTEXITCODE -ne 0) { throw "MSIX signing failed with exit code $LASTEXITCODE." }
    # Validate that the signed package remains readable without requiring this build
    # process to modify the user's certificate stores.
    Remove-BuildDirectory $verifyDir
    & $makeAppx unpack /p $packagePath /d $verifyDir
    if ($LASTEXITCODE -ne 0) { throw "MSIX package validation failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath (Join-Path $verifyDir 'AppxManifest.xml'))) {
        throw 'MSIX validation did not produce AppxManifest.xml.'
    }

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
    foreach ($file in @($installerFiles) + @('Veyron-v1.msix', 'Veyron-v1.cer')) {
        Copy-Item -LiteralPath (Join-Path $buildDir $file) -Destination (Join-Path $installerDir $file) -Force
    }
    $installerZip = Join-Path $buildDir 'Veyron-v1-Installer.zip'
    if (Test-Path -LiteralPath $installerZip) { Remove-Item -LiteralPath $installerZip -Force }
    Compress-Archive -Path (Join-Path $installerDir '*') -DestinationPath $installerZip -CompressionLevel Optimal

    Write-Host "Package output: $packagePath"
    Write-Host "Installer output: $installerZip"
    Write-Host 'Open Install-Veyron.vbs from the extracted installer folder to install and launch Veyron v1 without a terminal.'
}
finally {
    if (Test-Path -LiteralPath $certPath) { Remove-Item -LiteralPath $certPath -Force }
}
