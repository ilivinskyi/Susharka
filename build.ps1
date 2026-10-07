# Builds the portable exe and the installer into .\dist
#   dist\Susharka.exe                  portable single file
#   dist\Susharka-Setup-<version>.exe  installer (needs Inno Setup 6: winget install JRSoftware.InnoSetup)
# Usage: pwsh ./build.ps1 [-SkipTests] [-SkipInstaller]
param([switch]$SkipTests, [switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $SkipTests) {
    dotnet test tests/Susharka.Tests -c Release
    if ($LASTEXITCODE) { throw "Tests failed" }
}

if (Test-Path dist) { Remove-Item dist -Recurse -Force }
dotnet publish src/Susharka -c Release -o dist
if ($LASTEXITCODE) { throw "Publish failed" }
Get-ChildItem dist -Exclude Susharka.exe | Remove-Item -Recurse -Force

if (-not $SkipInstaller) {
    $version = ([xml](Get-Content src/Susharka/Susharka.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }
    & $iscc /Q "/DAppVersion=$version" installer/Susharka.iss
    if ($LASTEXITCODE) { throw "Installer build failed" }
}

Get-ChildItem dist -File | ForEach-Object { "{0,-34} {1,6:N1} MB" -f $_.Name, ($_.Length / 1MB) }
