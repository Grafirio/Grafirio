# Behavioral checks use disposable directories; Docker config does not start containers.
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixture = Join-Path $repo ('.quality/worktree-tests-' + [Guid]::NewGuid().ToString('N'))
function Assert-True($condition, $message) { if (!$condition) { throw $message } }
function Read-Environment($path) {
    $result = @{}
    foreach ($line in (Get-Content -LiteralPath $path)) {
        $key, $value = $line -split '=', 2
        $result[$key] = $value
    }
    return $result
}
foreach ($name in @('first', 'second', 'busy')) {
    $scripts = Join-Path $fixture "$name/scripts"
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'scripts/New-WorktreeEnvironment.ps1') -Destination $scripts
}
$firstScript = Join-Path $fixture 'first/scripts/New-WorktreeEnvironment.ps1'
$secondScript = Join-Path $fixture 'second/scripts/New-WorktreeEnvironment.ps1'
& $firstScript -Slot 901
& $secondScript -Slot 902
$firstFile = Join-Path $fixture 'first/.worktree/test.env'
$before = Get-Content -LiteralPath $firstFile -Raw
$first = Read-Environment $firstFile
$second = Read-Environment (Join-Path $fixture 'second/.worktree/test.env')
Assert-True ($first.COMPOSE_PROJECT_NAME -ne $second.COMPOSE_PROJECT_NAME) 'Worktree project identities collide'
Assert-True ($first.GRAFIRIO_TEST_PASSWORD -ne $second.GRAFIRIO_TEST_PASSWORD) 'Passwords were reused'
Assert-True ($first.GRAFIRIO_TEST_POSTGRES_PORT -ne $second.GRAFIRIO_TEST_POSTGRES_PORT) 'Ports collide'
$refused = $false
try { & $firstScript -Slot 904 } catch { $refused = $true }
Assert-True $refused 'Existing credentials were overwritten'
Assert-True ((Get-Content -LiteralPath $firstFile -Raw) -eq $before) 'Existing environment changed'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 29030)
try {
    $listener.Start()
    $refused = $false
    try { & (Join-Path $fixture 'busy/scripts/New-WorktreeEnvironment.ps1') -Slot 903 } catch { $refused = $true }
    Assert-True $refused 'Occupied port was accepted'
    Assert-True (!(Test-Path (Join-Path $fixture 'busy/.worktree/test.env'))) 'Busy environment was written'
} finally { $listener.Stop() }
if (Get-Command docker -ErrorAction SilentlyContinue) {
    $json = & docker compose --env-file $firstFile -f (Join-Path $repo 'docker-compose.worktree.yml') config --format json
    if ($LASTEXITCODE -ne 0) { throw 'Compose configuration rejected' }
    $compose = ($json -join "`n") | ConvertFrom-Json
    Assert-True ($compose.name -eq $first.COMPOSE_PROJECT_NAME) 'Compose project name not isolated'
    foreach ($service in $compose.services.PSObject.Properties.Value) {
        Assert-True (!$service.container_name) 'Fixed container name found'
        foreach ($port in $service.ports) { Assert-True ($port.host_ip -eq '127.0.0.1') 'Port exposed beyond loopback' }
    }
    foreach ($volume in $compose.volumes.PSObject.Properties.Value) {
        Assert-True ($volume.name.StartsWith($first.COMPOSE_PROJECT_NAME + '_')) 'Shared volume namespace found'
    }
    Write-Output 'PASS: Compose project, port and volume isolation'
}
Write-Output 'PASS: distinct environments, credential preservation and busy-port rejection'
