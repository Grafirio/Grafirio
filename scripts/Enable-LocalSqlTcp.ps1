[CmdletBinding()]
param([switch]$Apply, [string]$ResultPath)

$ErrorActionPreference = 'Stop'
$instanceName = 'SQLEXPRESS'
$serviceName = 'MSSQL$SQLEXPRESS'
$instance = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').$instanceName
if ($instance -notmatch '^MSSQL[0-9]+\.SQLEXPRESS$') { throw 'Unexpected SQL instance.' }
$nativePath = 'HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\' + $instance + '\MSSQLServer\SuperSocketNetLib\Tcp'
$tcpPath = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SQL Server\' + $instance + '\MSSQLServer\SuperSocketNetLib\Tcp'
if (!$Apply) {
    Get-ItemProperty -LiteralPath $tcpPath | Select-Object Enabled, ListenOnAllIPs
    Get-NetTCPConnection -State Listen -LocalPort 1433 -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort
    return
}
$administrator = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$administrator.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script as administrator.' }
$backup = Join-Path $env:TEMP ('Grafirio-SqlTcp-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.reg')
$changed = $false
try {
    if (@(Get-NetTCPConnection -State Listen -LocalPort 1433 -ErrorAction SilentlyContinue).Count) {
        throw 'Port 1433 is already in use; no configuration was changed.'
    }
    $loopbackEntries = @(Get-ChildItem -LiteralPath $tcpPath | ForEach-Object {
        $entry = Get-ItemProperty -LiteralPath $_.PSPath
        if ($entry.IpAddress -in @('127.0.0.1', '::1')) { $entry }
    })
    if (!$loopbackEntries.Count) { throw 'No loopback TCP entries found; no configuration was changed.' }
    & reg.exe export $nativePath $backup /y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'SQL TCP configuration backup failed.' }
    $changed = $true
    Set-ItemProperty -LiteralPath $tcpPath -Name Enabled -Value 1
    Set-ItemProperty -LiteralPath $tcpPath -Name ListenOnAllIPs -Value 0
    foreach ($entry in Get-ChildItem -LiteralPath $tcpPath) {
        if ($entry.PSChildName -eq 'IPAll') { continue }
        $address = Get-ItemProperty -LiteralPath $entry.PSPath
        $loopback = $address.IpAddress -in @('127.0.0.1', '::1')
        Set-ItemProperty -LiteralPath $entry.PSPath -Name Enabled -Value ([int]$loopback)
        if ($loopback) {
            Set-ItemProperty -LiteralPath $entry.PSPath -Name TcpDynamicPorts -Value ''
            Set-ItemProperty -LiteralPath $entry.PSPath -Name TcpPort -Value '1433'
        }
    }
    Restart-Service -Name $serviceName -ErrorAction Stop
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 1433 -ErrorAction SilentlyContinue)
        if ($listeners.Count) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$listeners.Count -or @($listeners | Where-Object { $_.LocalAddress -notin @('127.0.0.1', '::1') }).Count) {
        throw 'Loopback-only listener verification failed.'
    }
    $result = 'PASS: SQL TCP enabled on loopback only. Backup: ' + $backup
    Write-Output $result
    if ($ResultPath) { Set-Content -LiteralPath $ResultPath -Value $result -Encoding UTF8 }
}
catch {
    if ($changed) {
        & reg.exe import $backup | Out-Null
        if ($LASTEXITCODE -eq 0) { Restart-Service -Name $serviceName -ErrorAction Continue }
    }
    if ($ResultPath) { Set-Content -LiteralPath $ResultPath -Value ('FAIL: ' + $_.Exception.Message) -Encoding UTF8 }
    throw
}
