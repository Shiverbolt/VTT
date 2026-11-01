# Compiles the audio code with the offline tests and runs them.
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
Push-Location (Join-Path $PSScriptRoot "..")
try {
    New-Item -ItemType Directory -Force dist | Out-Null
    & "$fw\csc.exe" /nologo /out:dist\AudioTests.exe /r:System.Core.dll /r:"$fw\WPF\System.Speech.dll" tests\AudioTests.cs src\Audio.cs
    if ($LASTEXITCODE -ne 0) { throw "Test build failed" }
    & .\dist\AudioTests.exe
    exit $LASTEXITCODE
}
finally { Pop-Location }
