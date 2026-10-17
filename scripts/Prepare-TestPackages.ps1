# CI uses the same pinned shared source as the production Dockerfiles.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SharedSource,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$expected = (Get-Content (Join-Path $repo 'shared-packages.ref') -Raw).Trim()
if ($expected -notmatch '^[a-fA-F0-9]{40}$') { throw 'shared-packages.ref must contain a commit SHA.' }
$actual = & git -C $SharedSource rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual.Trim() -ne $expected) { throw 'Shared source does not match the pinned commit.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$packages = @(Get-ChildItem (Join-Path $SharedSource 'src') -Filter '*.csproj' -Recurse)
if (!$packages.Count) { throw 'No shared package projects found.' }
foreach ($project in $packages) {
    & dotnet pack $project.FullName -c Release -o $OutputDirectory '-p:RestoreSources=https://api.nuget.org/v3/index.json'
    if ($LASTEXITCODE -ne 0) { throw "Package build failed: $($project.Name)" }
}
# A separate configuration avoids modifying nuget.config or using GitHub feed credentials.
$config = Join-Path $OutputDirectory 'NuGet.Config'
$escaped = [System.Security.SecurityElement]::Escape([IO.Path]::GetFullPath($OutputDirectory))
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="local" value="$escaped" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8
Write-Output "Test NuGet configuration: $config"
