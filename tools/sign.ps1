# Signs the given files with the code-signing certificate in your user certificate store
# (creating a self-signed one on first use), then writes dist\SHA256SUMS.txt for them.
# A self-signed certificate proves the files are unchanged, but Windows still shows "Unknown publisher";
# swap in a certificate from a trusted CA (or Azure Trusted Signing) to remove that.
param(
    [string[]]$Files = @("dist\VTT.exe"),
    [string]$Subject = "CN=Shiverbolt (V.T.T? self-signed)",
    [switch]$NoSums
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5)
    Write-Host "Created certificate $($cert.Thumbprint)"
}

$sums = @()
foreach ($f in $Files) {
    $path = Join-Path $root $f
    $sig = Set-AuthenticodeSignature -FilePath $path -Certificate $cert -HashAlgorithm SHA256 -TimestampServer "http://timestamp.digicert.com"
    if (-not $sig.SignerCertificate) { throw "Signing failed for $f" }
    Write-Host "Signed $f ($($sig.Status); 'UnknownError' is expected for a self-signed root)"
    $sums += "{0}  {1}" -f (Get-FileHash $path -Algorithm SHA256).Hash.ToLower(), (Split-Path $path -Leaf)
}

Export-Certificate -Cert $cert -FilePath (Join-Path $root "dist\VTT-codesign.cer") | Out-Null
if (-not $NoSums) {
    Set-Content -Path (Join-Path $root "dist\SHA256SUMS.txt") -Value $sums -Encoding ascii
    $sums | ForEach-Object { Write-Host $_ }
}
