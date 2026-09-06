[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $FilePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$thumbprint = $env:GRAFIRIO_SIGNING_CERTIFICATE_THUMBPRINT
if ([string]::IsNullOrWhiteSpace($thumbprint) -or $thumbprint -notmatch '^[A-Fa-f0-9]{40}$') {
    throw 'A valid GRAFIRIO_SIGNING_CERTIFICATE_THUMBPRINT is required for signing.'
}

$certificate = Get-Item "Cert:\CurrentUser\My\$thumbprint" -ErrorAction Stop
if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) {
    throw 'The code-signing certificate must have a private key and must not be expired.'
}

$signature = Set-AuthenticodeSignature -FilePath $FilePath -Certificate $certificate `
    -HashAlgorithm SHA256 -TimestampServer $env:GRAFIRIO_SIGNING_TIMESTAMP_SERVER
if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) {
    throw "Signing or timestamp verification failed for '$FilePath': $($signature.Status)."
}