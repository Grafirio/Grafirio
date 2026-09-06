[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installerDirectory = Split-Path $PSScriptRoot -Parent
$buildPath = Join-Path $installerDirectory 'Build-Installer.ps1'
$setupPath = Join-Path $installerDirectory 'GrafirioSetup.iss'
$setup = (Get-Content -LiteralPath $setupPath -Raw).Replace("`r`n", "`n")
$passed = 0

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
    $script:passed++
}

function Assert-Throws {
    param([scriptblock] $Action, [string] $ExpectedMessage)
    try { & $Action }
    catch {
        Assert-Condition ($_.Exception.Message -like $ExpectedMessage) "Unexpected failure: $($_.Exception.Message)"
        return
    }
    throw "Expected failure: $ExpectedMessage"
}

$tokens = $null
$parseErrors = $null
$syntax = [System.Management.Automation.Language.Parser]::ParseFile($buildPath, [ref] $tokens, [ref] $parseErrors)
Assert-Condition ($parseErrors.Count -eq 0) 'Build script must parse in Windows PowerShell.'

# Load only pure validation helpers so these tests never publish, download, or install anything.
foreach ($name in @('Resolve-InnoCompiler', 'Assert-OfflineRuntime')) {
    $function = $syntax.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    . ([scriptblock]::Create($function.Extent.Text))
}

Assert-Throws { Resolve-InnoCompiler (Join-Path $PSScriptRoot 'missing-compiler.exe') } 'Inno Setup compiler not found:*'
Assert-Throws { Assert-OfflineRuntime $buildPath } '*valid Microsoft Corporation Authenticode signature*'

$fakeSubject = 'CN=Microsoft Corporation, O=Microsoft Corporation, C=US'
$fakeStatus = 'Valid'
$fakeLength = 100MB
function Get-AuthenticodeSignature {
    param([string] $LiteralPath)
    return [pscustomobject]@{
        Status = $script:fakeStatus
        SignerCertificate = [pscustomobject]@{ Subject = $script:fakeSubject }
    }
}
function Get-Item {
    param([string] $LiteralPath)
    return [pscustomobject]@{
        Length = $script:fakeLength
        VersionInfo = [pscustomobject]@{ ProductName = 'Microsoft Edge Update' }
    }
}

Assert-OfflineRuntime 'test-runtime.exe'
$passed++
$fakeLength = 2MB
Assert-Throws { Assert-OfflineRuntime 'test-runtime.exe' } '*not the online bootstrapper*'
$fakeLength = 100MB
$fakeSubject = 'CN=Another Publisher, O=Another Publisher, C=US'
Assert-Throws { Assert-OfflineRuntime 'test-runtime.exe' } '*valid Microsoft Corporation Authenticode signature*'
$fakeSubject = 'CN=Microsoft Corporation, O=Microsoft Corporation, C=US'
$fakeStatus = 'HashMismatch'
Assert-Throws { Assert-OfflineRuntime 'test-runtime.exe' } '*valid Microsoft Corporation Authenticode signature*'

foreach ($contract in @(
    '(?m)^PrivilegesRequired=lowest$',
    '(?m)^DefaultDirName=\{localappdata\}\\Programs\\Grafirio$',
    '(?m)^AppId=\{\{C594931A-BBE0-47CB-A88B-D6C024783C65\}$',
    '(?m)^CloseApplications=yes$',
    '(?m)^UsePreviousTasks=yes$',
    '(?m)^UsePreviousAppDir=yes$',
    '(?m)^OutputBaseFilename=GrafirioSetup$',
    'Name: "startup";[^\r\n]*Flags: unchecked',
    '\{userprograms\}\\Grafirio',
    '\{userdesktop\}\\Grafirio',
    '\{userstartup\}\\Grafirio',
    'onlyifdoesntexist uninsneveruninstall',
    'HasWebView2Version\(HKCU32\) or HasWebView2Version\(HKLM32\)',
    "'/silent /install'",
    'Result := not IsAdmin;'
)) {
    Assert-Condition ($setup -match $contract) "Missing installer contract: $contract"
}
Assert-Condition ($setup -notmatch '(?im)^\[UninstallDelete\]|taskkill|CloseApplications=force|Type: filesandordirs') 'Installer must not delete user data or force-kill applications.'
Write-Host "Installer checks passed: $passed"