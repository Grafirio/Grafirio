[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string] $Version = '1.0.0',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\..\..\artifacts\bridge-installer'),
    [string] $IsccPath,
    [string] $WebView2InstallerPath,
    [string] $SigningCertificateThumbprint,
    [uri] $TimestampServer = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-InnoCompiler {
    param([string] $RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (-not (Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "Inno Setup compiler not found: $RequestedPath"
        }
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw 'Inno Setup 6.7.3 or later is required. Supply -IsccPath or put ISCC.exe on PATH. This script does not install build tools.'
}

function Assert-OfflineRuntime {
    param([string] $Path)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
        throw 'WebView2 prerequisite must have a valid Microsoft Corporation Authenticode signature.'
    }

    $file = Get-Item -LiteralPath $Path
    # The online bootstrapper is also Microsoft-signed, but cannot support offline installation.
    $minimumOfflineSize = 50MB
    if ($file.Length -lt $minimumOfflineSize -or $file.VersionInfo.ProductName -ne 'Microsoft Edge Update') {
        throw 'Expected the full WebView2 Evergreen Standalone Installer, not the online bootstrapper.'
    }
}

$compiler = Resolve-InnoCompiler $IsccPath
$null = Get-Command dotnet -ErrorAction Stop
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $outputPath -Force
# Each build gets fresh staging so removed publish files never leak into the next installer.
$stagingPath = Join-Path $outputPath ([Guid]::NewGuid().ToString('N'))
$publishPath = Join-Path $stagingPath 'publish'
$compiledPath = Join-Path $stagingPath 'setup'
$null = New-Item -ItemType Directory -Path $stagingPath -Force
$previousThumbprint = $env:GRAFIRIO_SIGNING_CERTIFICATE_THUMBPRINT
$previousTimestampServer = $env:GRAFIRIO_SIGNING_TIMESTAMP_SERVER

try {
    if ([string]::IsNullOrWhiteSpace($WebView2InstallerPath)) {
        $WebView2InstallerPath = Join-Path $stagingPath 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
        $offlineRuntimeUrl = 'https://go.microsoft.com/fwlink/?linkid=2124701'
        Write-Host 'Downloading the official Microsoft WebView2 offline x64 runtime.'
        Invoke-WebRequest -Uri $offlineRuntimeUrl -OutFile $WebView2InstallerPath -UseBasicParsing
    }
    $WebView2InstallerPath = (Resolve-Path -LiteralPath $WebView2InstallerPath).Path
    Assert-OfflineRuntime $WebView2InstallerPath

    $projectPath = Join-Path $PSScriptRoot '..\Grafirio.Bridge.Desktop\Grafirio.Bridge.Desktop.csproj'
    & dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false "-p:Version=$Version" -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed with exit code $LASTEXITCODE." }

    $desktopExecutable = Join-Path $publishPath 'GrafirioBridge.Desktop.exe'
    if (-not (Test-Path -LiteralPath $desktopExecutable -PathType Leaf)) {
        throw 'Desktop publish did not produce GrafirioBridge.Desktop.exe.'
    }

    $compilerArguments = @(
        "/DPublishDirectory=$publishPath",
        "/DWebView2Installer=$WebView2InstallerPath",
        "/DOutputDirectory=$compiledPath",
        "/DAppVersion=$Version"
    )
    if (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
        $env:GRAFIRIO_SIGNING_CERTIFICATE_THUMBPRINT = $SigningCertificateThumbprint
        $env:GRAFIRIO_SIGNING_TIMESTAMP_SERVER = $TimestampServer.AbsoluteUri
        $signingScript = Join-Path $PSScriptRoot 'Sign-Installer.ps1'
        & $signingScript -FilePath $desktopExecutable
        $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $signingCommand = '$q' + $windowsPowerShell + '$q -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $q' +
            $signingScript + '$q -FilePath $f'
        $compilerArguments += '/DSigningEnabled=1'
        $compilerArguments += "/Sgrafirio=$signingCommand"
    }
    else {
        Write-Warning 'No code-signing certificate supplied. Setup will be unsigned; Windows SmartScreen may warn users.'
    }

    & $compiler @compilerArguments (Join-Path $PSScriptRoot 'GrafirioSetup.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

    $installerPath = Join-Path $compiledPath 'GrafirioSetup.exe'
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw 'Inno Setup did not produce GrafirioSetup.exe.'
    }
    if ($SigningCertificateThumbprint -and (Get-AuthenticodeSignature $installerPath).Status -ne 'Valid') {
        throw 'The generated installer signature is invalid.'
    }

    $destination = Join-Path $outputPath 'GrafirioSetup.exe'
    Copy-Item -LiteralPath $installerPath -Destination $destination -Force
    Write-Host "Installer ready: $destination"
    Get-FileHash -LiteralPath $destination -Algorithm SHA256
}
finally {
    $env:GRAFIRIO_SIGNING_CERTIFICATE_THUMBPRINT = $previousThumbprint
    $env:GRAFIRIO_SIGNING_TIMESTAMP_SERVER = $previousTimestampServer
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}