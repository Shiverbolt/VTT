# Signs dist\VTT.exe with the code-signing certificate in your user certificate store
# (creating a self-signed one on first use), then writes the release checksums.
# A self-signed certificate proves the file is unchanged, but Windows still shows "Unknown publisher";
# swap in a certificate from a trusted CA (or Azure Trusted Signing) to remove that.
param([string]$Subject = "CN=Shiverbolt (V.T.T? self-signed)")
$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot ".."
$exe = Join-Path $root "dist\VTT.exe"

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5)
    Write-Host "Created certificate $($cert.Thumbprint)"
}

$sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256 -TimestampServer "http://timestamp.digicert.com"
Write-Host "Signed: $($sig.SignerCertificate.Subject) (status: $($sig.Status) - 'UnknownError' is expected for a self-signed root)"

Export-Certificate -Cert $cert -FilePath (Join-Path $root "dist\VTT-codesign.cer") | Out-Null
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
Set-Content -Path (Join-Path $root "dist\SHA256SUMS.txt") -Value "$hash  VTT.exe" -Encoding ascii
Write-Host "SHA256 $hash"
