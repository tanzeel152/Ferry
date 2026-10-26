# Builds publish\Ferry.exe, then dist\Ferry-Setup-<version>.exe.
# Needs the .NET 8 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
# -SkipPublish packages the Ferry.exe already in publish\ (the release workflow signs it first).
param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $SkipPublish) {
    dotnet publish Ferry\Ferry.csproj -c Release -o publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }

& $iscc /Q installer\Ferry.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Get-ChildItem dist\Ferry-Setup-*.exe | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object { "Built $($_.FullName)" }
