# Builds the release: signed VTT.exe (portable), then the installer with that exe inside it, signed too,
# plus SHA256SUMS.txt and the public certificate in dist\.
$ErrorActionPreference = "Stop"
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$version = "1.0.0-beta.4"
$setup = "VTT-Setup-$version.exe"

Push-Location $root
try {
    & .\build.ps1
    & .\tools\sign.ps1 -Files "dist\VTT.exe" -NoSums

    & "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize /out:"dist\$setup" /win32icon:assets\vtt.ico `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        /resource:dist\VTT.exe,VTT.exe /resource:LICENSE,LICENSE.txt `
        installer\Setup.cs src\Ui.cs
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    Write-Host "Built dist\$setup"

    & .\tools\sign.ps1 -Files "dist\$setup" -NoSums

    # The portable VTT.exe is byte-identical to the copy inside the installer.
    $sums = foreach ($f in $setup, "VTT.exe") { "{0}  {1}" -f (Get-FileHash "dist\$f" -Algorithm SHA256).Hash.ToLower(), $f }
    # LF line endings so `sha256sum -c` works on Linux, macOS and Git Bash too.
    [IO.File]::WriteAllText((Join-Path $root "dist\SHA256SUMS.txt"), (($sums -join "`n") + "`n"), (New-Object Text.ASCIIEncoding))
    $sums | ForEach-Object { Write-Host $_ }
}
finally { Pop-Location }
