# Creates a local self-signed code-signing certificate in CurrentUser\My for testing only.
# Private key remains in the Windows certificate store and is NEVER exported to disk.
[CmdletBinding()]
param(
    [int]$ValidYears = 2
)

$ErrorActionPreference = "Stop"

Write-Warning "Creating a self-signed TEST code-signing certificate (CN=James God) in Cert:\CurrentUser\My. DO NOT use for production releases."

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject "CN=James God" `
    -FriendlyName "JamesOptimizer Local Test Signing Cert" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears($ValidYears)

Write-Host "Created test certificate:" -ForegroundColor Cyan
Write-Host "  Subject   : $($cert.Subject)"
Write-Host "  Store     : Cert:\CurrentUser\My"
Write-Host "  NotAfter  : $($cert.NotAfter)"
Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
Write-Output $cert.Thumbprint
