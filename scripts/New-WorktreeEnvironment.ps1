[CmdletBinding()]
param([ValidateRange(1, 999)][int]$Slot)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($repo.ToLowerInvariant()))).ToLowerInvariant()
if (!$Slot) { $Slot = 1 + ([Convert]::ToInt32($hash.Substring(0, 4), 16) % 999) }
$directory = Join-Path $repo '.worktree'
$file = Join-Path $directory 'test.env'
if (Test-Path -LiteralPath $file) { throw 'A test environment already exists. Reuse its file; do not rotate credentials of existing volumes.' }
$basePort = 20000 + $Slot * 10
$listeners = @()
try {
    foreach ($port in @($basePort, ($basePort + 1), ($basePort + 2), ($basePort + 3))) {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
        $listener.Start()
        $listeners += $listener
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $password = 'Gf!9' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    @"
COMPOSE_PROJECT_NAME=grafirio-test-$($hash.Substring(0, 12))
GRAFIRIO_TEST_DATABASE=GrafirioEval_$($hash.Substring(0, 12))
GRAFIRIO_TEST_PASSWORD=$password
GRAFIRIO_TEST_POSTGRES_PORT=$basePort
GRAFIRIO_TEST_REDIS_PORT=$($basePort + 1)
GRAFIRIO_TEST_SQL_PORT=$($basePort + 2)
GRAFIRIO_BROWSER_PORT=$($basePort + 3)
"@ | Set-Content -LiteralPath $file -Encoding utf8
} finally {
    foreach ($listener in $listeners) { $listener.Stop() }
}
Write-Output "Created $file (local test credentials, ignored by Git). No containers started."
Write-Output "Ports: PostgreSQL $basePort, Redis $($basePort + 1), SQL $($basePort + 2), browser $($basePort + 3)."
