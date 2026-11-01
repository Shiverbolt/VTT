# Builds dist\VTT.exe with the C# compiler that ships with Windows (.NET Framework 4.x) - no SDK needed.
$ErrorActionPreference = "Stop"
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path assets\vtt.ico)) { & .\tools\make-icon.ps1 }
    New-Item -ItemType Directory -Force dist | Out-Null
    & "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /optimize /out:dist\VTT.exe /win32icon:assets\vtt.ico `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        /r:"$fw\WPF\System.Speech.dll" src\*.cs
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    Write-Host "Built dist\VTT.exe"
}
finally { Pop-Location }
