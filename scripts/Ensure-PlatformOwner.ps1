[CmdletBinding()]
# Read-only unless -Apply is supplied. Pin the approved identity by UUID AND email;
# never grant ownership to a newly registered account merely matching an email.
param([string]$ResourceGroup = 'grifirio-rg', [switch]$Apply)

$ErrorActionPreference = 'Stop'
$stage = 'configuration'
$baseUrl = 'https://login.grafirio.com'
$realm = 'grafirio'

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

$ownerId = '3ebc7f32-9975-4b38-a3c6-2f8533adc9d3'
$ownerEmail = 'fatih@grafirio.com'
$groupName = 'grafirio-platform-owners'
$realmPath = '/admin/realms/' + $realm
try {
    $stage = 'administrator authentication'
    $settings = Read-AzureJson @('containerapp', 'show', '-g', $ResourceGroup, '-n', 'keycloak',
        '--query', 'properties.template.containers[0].env[?name==`KEYCLOAK_ADMIN` || name==`KEYCLOAK_ADMIN_PASSWORD`]')
    $token = Invoke-RestMethod -Uri ($baseUrl + '/realms/master/protocol/openid-connect/token') `
        -Method Post -ContentType 'application/x-www-form-urlencoded' -TimeoutSec 30 -MaximumRedirection 0 -Body @{
            grant_type = 'password'; client_id = 'admin-cli'
            username = (Read-Setting $settings 'KEYCLOAK_ADMIN' 'keycloak')
            password = (Read-Setting $settings 'KEYCLOAK_ADMIN_PASSWORD' 'keycloak')
        }
    $headers = @{ Authorization = ('Bearer ' + $token.access_token) }
    $stage = 'identity and scope checks'
    $userPath = $realmPath + '/users/' + $ownerId
    $owner = Invoke-Keycloak Get $userPath $headers
    if ($owner.id -ne $ownerId -or $owner.email -ine $ownerEmail -or !$owner.enabled) {
        throw 'Owner identity mismatch.'
    }
    foreach ($role in @('ADMIN', 'PLATFORM_ADMIN')) {
        if ($role -notin @($owner.attributes.business_roles)) { throw 'Expected existing role missing.' }
    }
    $clients = @(Invoke-Keycloak Get ($realmPath + '/clients?clientId=grafirio-client') $headers)
    if ($clients.Count -ne 1 -or $clients[0].clientId -ne 'grafirio-client') { throw 'Unexpected client.' }
    $clientPath = $realmPath + '/clients/' + $clients[0].id
    $mappers = @(Invoke-Keycloak Get ($clientPath + '/protocol-mappers/models') $headers)
    $matching = @($mappers | Where-Object { $_.config.'claim.name' -eq 'business_roles' })
    if ($matching.Count -ne 1) { throw 'Ambiguous business role mapper.' }
    $mapper = $matching[0]
    if ($mapper.protocolMapper -ne 'oidc-usermodel-attribute-mapper' -or
        $mapper.config.'user.attribute' -ne 'business_roles' -or $mapper.config.multivalued -ne 'true') {
        throw 'Unexpected business role mapper.'
    }
    # Enabling aggregation must not activate latent roles in unrelated groups.
    # Fail closed on any other group; review its attributes before extending this migration.
    $groups = @(Invoke-Keycloak Get ($realmPath + '/groups?briefRepresentation=false&max=1000') $headers)
    if (@($groups | Where-Object { $_.name -ne $groupName -or $_.subGroupCount -gt 0 -or $_.subGroups.Count -gt 0 }).Count -gt 0) {
        throw 'Other groups require a separate aggregation review.'
    }
    $found = @($groups | Where-Object name -eq $groupName)
    if ($found.Count -gt 1) { throw 'Ambiguous owner group.' }
    if ($Apply -and $found.Count -eq 0) {
        $stage = 'create owner group'
        $null = Invoke-Keycloak Post ($realmPath + '/groups') $headers @{
            name = $groupName; attributes = @{ business_roles = @('ADMIN', 'PLATFORM_ADMIN') }
        }
        $found = @(Invoke-Keycloak Get ($realmPath + '/groups?search=' + $groupName + '&exact=true&briefRepresentation=false') $headers)
    }
    if ($found.Count -ne 1) { throw 'Owner group missing; run with -Apply.' }
    $group = Invoke-Keycloak Get ($realmPath + '/groups/' + $found[0].id) $headers
    if (@(Compare-Object @('ADMIN', 'PLATFORM_ADMIN') @($group.attributes.business_roles)).Count -gt 0) {
        throw 'Unexpected group roles.'
    }
    $members = @(Invoke-Keycloak Get ($realmPath + '/groups/' + $group.id + '/members?max=1000') $headers)
    if (@($members | Where-Object id -ne $ownerId).Count -gt 0) { throw 'Unexpected owner group member.' }
    if ($Apply) {
        $stage = 'enable group role inheritance'
        $mapper.config | Add-Member -NotePropertyName 'aggregate.attrs' -NotePropertyValue 'true' -Force
        $null = Invoke-Keycloak Put ($clientPath + '/protocol-mappers/models/' + $mapper.id) $headers $mapper
        $stage = 'assign owner group'
        $null = Invoke-Keycloak Put ($userPath + '/groups/' + $group.id) $headers
    }
    $stage = 'readback and token evaluation'
    $membership = @(Invoke-Keycloak Get ($userPath + '/groups') $headers)
    $readMapper = Invoke-Keycloak Get ($clientPath + '/protocol-mappers/models/' + $mapper.id) $headers
    $example = Invoke-Keycloak Get ($clientPath + '/evaluate-scopes/generate-example-access-token?userId=' + $ownerId) $headers
    if ($group.id -notin @($membership.id) -or $readMapper.config.'aggregate.attrs' -ne 'true' -or
        'PLATFORM_ADMIN' -notin @($example.business_roles) -or 'ADMIN' -notin @($example.business_roles)) {
        throw 'Owner inheritance verification failed.'
    }
    [pscustomobject]@{ email = $ownerEmail; id = $ownerId; group = $groupName;
        business_roles = $example.business_roles; verified = $true } | ConvertTo-Json
}
catch {
    $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'n/a' }
    throw ('Owner configuration failed at {0} (HTTP {1}). Sensitive response details suppressed.' -f $stage, $status)
}
finally { $settings = $token = $headers = $null }

