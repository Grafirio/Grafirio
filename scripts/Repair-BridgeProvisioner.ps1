[CmdletBinding()]
param([string]$ResourceGroup = 'grifirio-rg', [switch]$Repair, [switch]$SmokeTest)

$ErrorActionPreference = 'Stop'
$stage = 'configuration'
$probeId = $null
$baseUrl = 'https://login.grafirio.com'
$realm = 'grafirio'
$clientName = 'bridge-provisioner'

function Read-AzureJson {
    param([string[]]$Arguments)
    $output = & az @Arguments --only-show-errors -o json 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Azure configuration could not be read.' }
    return ($output -join [Environment]::NewLine | ConvertFrom-Json)
}

function Read-Setting {
    param($Settings, [string]$Name, [string]$App)
    $entries = @($Settings | Where-Object { $_.name -ieq $Name })
    if ($entries.Count -ne 1) { throw ('Missing or ambiguous setting: ' + $Name) }
    $entry = $entries[0]
    if (!$entry.secretRef) { return $entry.value }
    if ($entry.secretRef -notmatch '^[a-z0-9-]+$') { throw 'Invalid secret reference.' }
    $query = '[?name==''{0}''].value | [0]' -f $entry.secretRef
    return Read-AzureJson @('containerapp', 'secret', 'list', '-g', $ResourceGroup, '-n', $App,
        '--show-values', '--query', $query)
}

function Invoke-Keycloak {
    param([string]$Method, [string]$Path, $Headers, $Body)
    $request = @{ Uri = ($baseUrl + $Path); Method = $Method; Headers = $Headers; TimeoutSec = 30; MaximumRedirection = 0 }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json'
        $request.Body = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Body -Depth 15 -Compress))
    }
    $result = Invoke-RestMethod @request
    return $result
}

try {
    $settings = Read-AzureJson @('containerapp', 'show', '-g', $ResourceGroup, '-n', 'data-analysis-api',
        '--query', 'properties.template.containers[0].env[?starts_with(name, `Keycloak__`)]')
    if ((Read-Setting $settings 'Keycloak__BaseUrl' 'data-analysis-api').TrimEnd('/') -ne $baseUrl `
        -or (Read-Setting $settings 'Keycloak__Realm' 'data-analysis-api') -ne $realm `
        -or (Read-Setting $settings 'Keycloak__AdminClientId' 'data-analysis-api') -ne $clientName) {
        throw 'Unexpected identity target; credentials were not sent.'
    }
    $clientSecret = Read-Setting $settings 'Keycloak__AdminClientSecret' 'data-analysis-api'
    if ([string]::IsNullOrWhiteSpace($clientSecret) -or $clientSecret.Length -lt 24) { throw 'Missing or weak provisioner secret.' }
    $realmPath = '/admin/realms/' + $realm
    if ($Repair) {
        $stage = 'administrator authentication'
        $adminSettings = Read-AzureJson @('containerapp', 'show', '-g', $ResourceGroup, '-n', 'keycloak',
            '--query', 'properties.template.containers[0].env[?name==`KEYCLOAK_ADMIN` || name==`KEYCLOAK_ADMIN_PASSWORD`]')
        $adminToken = Invoke-RestMethod -Uri ($baseUrl + '/realms/master/protocol/openid-connect/token') `
            -Method Post -ContentType 'application/x-www-form-urlencoded' -TimeoutSec 30 -MaximumRedirection 0 -Body @{
                grant_type = 'password'; client_id = 'admin-cli'
                username = (Read-Setting $adminSettings 'KEYCLOAK_ADMIN' 'keycloak')
                password = (Read-Setting $adminSettings 'KEYCLOAK_ADMIN_PASSWORD' 'keycloak')
            }
        $adminHeaders = @{ Authorization = ('Bearer ' + $adminToken.access_token) }
        $adminSettings = $null
        $stage = 'provisioner configuration'
        $clients = @(Invoke-Keycloak Get ($realmPath + '/clients?clientId=' + $clientName) $adminHeaders)
        $clients = @($clients | Where-Object clientId -eq $clientName)
        if ($clients.Count -gt 1) { throw 'Ambiguous provisioner.' }
        if ($clients.Count -eq 0) {
            $null = Invoke-Keycloak Post ($realmPath + '/clients') $adminHeaders @{
                clientId = $clientName; name = 'Grafirio Bridge provisioner'; protocol = 'openid-connect'
                enabled = $true; publicClient = $false; clientAuthenticatorType = 'client-secret'
                serviceAccountsEnabled = $true; standardFlowEnabled = $false; implicitFlowEnabled = $false
                directAccessGrantsEnabled = $false; fullScopeAllowed = $false
                defaultClientScopes = @('roles'); optionalClientScopes = @(); secret = $clientSecret
            }
            Write-Output 'Created missing provisioner using the existing backend secret.'
            $clients = @(Invoke-Keycloak Get ($realmPath + '/clients?clientId=' + $clientName) $adminHeaders)
            $clients = @($clients | Where-Object clientId -eq $clientName)
        }
        if ($clients.Count -ne 1) { throw 'Provisioner not found after creation.' }
        $provisioner = $clients[0]
        if (!$provisioner.enabled -or $provisioner.publicClient -or !$provisioner.serviceAccountsEnabled `
            -or $provisioner.standardFlowEnabled -or $provisioner.directAccessGrantsEnabled -or $provisioner.fullScopeAllowed) {
            throw 'Existing client has unexpected security settings; manual review required.'
        }
        $clientPath = $realmPath + '/clients/' + $provisioner.id
        $existingSecret = Invoke-Keycloak Get ($clientPath + '/client-secret') $adminHeaders
        if ($existingSecret.value -cne $clientSecret) { throw 'Existing secret differs; automatic rotation refused.' }
        $existingSecret = $null
        $management = @(Invoke-Keycloak Get ($realmPath + '/clients?clientId=realm-management') $adminHeaders)
        $management = @($management | Where-Object clientId -eq 'realm-management')
        if ($management.Count -ne 1) { throw 'Realm management client not found.' }
        $managementId = $management[0].id
        $role = Invoke-Keycloak Get ($realmPath + '/clients/' + $managementId + '/roles/manage-clients') $adminHeaders
        $account = Invoke-Keycloak Get ($clientPath + '/service-account-user') $adminHeaders
        $stage = 'provisioner permissions'
        $null = Invoke-Keycloak Post ($realmPath + '/users/' + $account.id + '/role-mappings/clients/' + $managementId) $adminHeaders @($role)
        $null = Invoke-Keycloak Post ($clientPath + '/scope-mappings/clients/' + $managementId) $adminHeaders @($role)
        Write-Output 'Ensured manage-clients assignment and token scope; no realm-admin role granted.'
    }
    $stage = 'service authentication'
    $serviceToken = Invoke-RestMethod -Uri ($baseUrl + '/realms/' + $realm + '/protocol/openid-connect/token') `
        -Method Post -ContentType 'application/x-www-form-urlencoded' -TimeoutSec 30 -MaximumRedirection 0 `
        -Body @{ grant_type = 'client_credentials'; client_id = $clientName; client_secret = $clientSecret }
    $serviceHeaders = @{ Authorization = ('Bearer ' + $serviceToken.access_token) }
    $stage = 'service authorization'
    $found = @(Invoke-Keycloak Get ($realmPath + '/clients?clientId=' + $clientName) $serviceHeaders)
    if (@($found | Where-Object clientId -eq $clientName).Count -ne 1) { throw 'Client lookup failed.' }
    Write-Output 'PASS: service token and client lookup.'
    if ($SmokeTest) {
        $stage = 'client lifecycle smoke test'
        $probeName = 'grafirio-provisioner-probe-' + [Guid]::NewGuid().ToString('N')
        $null = Invoke-Keycloak Post ($realmPath + '/clients') $serviceHeaders @{
            clientId = $probeName; protocol = 'openid-connect'; enabled = $true; publicClient = $false
            serviceAccountsEnabled = $true; standardFlowEnabled = $false; directAccessGrantsEnabled = $false
        }
        $probe = @(Invoke-Keycloak Get ($realmPath + '/clients?clientId=' + $probeName) $serviceHeaders)
        $probe = @($probe | Where-Object clientId -eq $probeName)
        if ($probe.Count -ne 1) { throw 'Probe lookup failed.' }
        $probeId = $probe[0].id
        $probeSecret = Invoke-Keycloak Get ($realmPath + '/clients/' + $probeId + '/client-secret') $serviceHeaders
        if ([string]::IsNullOrWhiteSpace($probeSecret.value)) { throw 'Probe secret read failed.' }
        $null = Invoke-Keycloak Put ($realmPath + '/clients/' + $probeId) $serviceHeaders @{ enabled = $false }
        $null = Invoke-Keycloak Delete ($realmPath + '/clients/' + $probeId) $serviceHeaders
        $probeId = $null
        Write-Output 'PASS: create, lookup, secret read, disable and cleanup of disposable probe client.'
    }
    Write-Output 'Provisioner verification succeeded. Backend secret and deployment were not changed.'
}
catch {
    Write-Warning ('Failed script line: ' + $_.InvocationInfo.ScriptLineNumber)
    $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'n/a' }
    throw ('Bridge provisioner operation failed at {0} (HTTP {1}). Sensitive response details suppressed.' -f $stage, $status)
}
finally {
    if ($probeId -and $serviceHeaders) {
        try { $null = Invoke-Keycloak Delete ($realmPath + '/clients/' + $probeId) $serviceHeaders }
        catch { Write-Warning ('Probe cleanup requires attention: ' + $probeId) }
    }
    $clientSecret = $adminSettings = $adminHeaders = $serviceHeaders = $adminToken = $serviceToken = $probeSecret = $null
}
