# Builds everything into .\dist
#   dist\Susharka.exe                     portable single file
#   dist\Susharka-Setup-<version>.exe     installer (needs Inno Setup 6: winget install JRSoftware.InnoSetup)
#   dist\Susharka-<version>-x64.msix      Microsoft Store package (identity from installer\msix\store.json, git-ignored)
#
# Usage: pwsh ./build.ps1 [-SkipTests] [-SkipInstaller] [-SkipMsix] [-RegisterMsix]
#   -RegisterMsix  installs the unpacked MSIX layout on this PC for testing (needs Developer Mode)
param([switch]$SkipTests, [switch]$SkipInstaller, [switch]$SkipMsix, [switch]$RegisterMsix)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content src/Susharka/Susharka.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (-not $SkipTests) {
    dotnet test tests/Susharka.Tests -c Release
    if ($LASTEXITCODE) { throw "Tests failed" }
}

# ── Portable exe ──────────────────────────────────────────────────────────────
if (Test-Path dist) { Remove-Item dist -Recurse -Force }
dotnet publish src/Susharka -c Release -o dist
if ($LASTEXITCODE) { throw "Publish failed" }
Get-ChildItem dist -Exclude Susharka.exe | Remove-Item -Recurse -Force

# ── Setup.exe ─────────────────────────────────────────────────────────────────
if (-not $SkipInstaller) {
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }
    & $iscc /Q "/DAppVersion=$version" installer/Susharka.iss
    if ($LASTEXITCODE) { throw "Installer build failed" }
}

# ── MSIX for the Microsoft Store ──────────────────────────────────────────────
if (-not $SkipMsix) {
    # makeappx/makepri come from the Microsoft.Windows.SDK.BuildTools NuGet package; no SDK install needed.
    $toolsDir = Join-Path $PSScriptRoot '.tools\buildtools'
    if (-not (Test-Path $toolsDir)) {
        New-Item -ItemType Directory -Force .tools | Out-Null
        Invoke-WebRequest https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools -OutFile .tools\buildtools.zip
        Expand-Archive .tools\buildtools.zip $toolsDir -Force
    }
    $makeappx = Get-ChildItem $toolsDir -Recurse -Filter makeappx.exe | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
    $makepri = Get-ChildItem $toolsDir -Recurse -Filter makepri.exe | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1

    $work = Join-Path $PSScriptRoot 'obj\msix'
    $layout = Join-Path $work 'layout'
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }

    # Inside a package the app runs from its install folder, so publish loose files (faster start than single-file).
    dotnet publish src/Susharka -c Release -r win-x64 --self-contained -o $layout `
        -p:PublishSingleFile=false -p:EnableCompressionInSingleFile=false
    if ($LASTEXITCODE) { throw "MSIX publish failed" }
    Copy-Item installer\msix\Assets (Join-Path $layout 'Assets') -Recurse

    # The Partner Center identity lives in installer\msix\store.json, which is git-ignored (see store.example.json).
    $store = if (Test-Path installer\msix\store.json) { Get-Content installer\msix\store.json -Raw | ConvertFrom-Json }
    $isStore = $store -and $store.IdentityName -and $store.Publisher -and $store.PublisherDisplayName
    $identity = if ($isStore) { $store } else {
        Write-Warning "No Partner Center identity in installer\msix\store.json: building with a local test identity (not uploadable)."
        [pscustomobject]@{ IdentityName = 'Susharka.LocalTest'; Publisher = 'CN=SusharkaLocalTest'; PublisherDisplayName = 'Susharka' }
    }
    $parts = @($version.Split('.')) + @('0', '0', '0')
    $msixVersion = ($parts[0..2] -join '.') + '.0'   # the Store requires the 4th part to be 0

    (Get-Content installer\msix\AppxManifest.template.xml -Raw) `
        -replace '\$IdentityName\$', [Security.SecurityElement]::Escape($identity.IdentityName) `
        -replace '\$Publisher\$', [Security.SecurityElement]::Escape($identity.Publisher) `
        -replace '\$PublisherDisplayName\$', [Security.SecurityElement]::Escape($identity.PublisherDisplayName) `
        -replace '\$Version\$', $msixVersion |
        Set-Content (Join-Path $layout 'AppxManifest.xml') -Encoding utf8

    # resources.pri lets Windows pick the right logo scale / target size.
    $priConfig = Join-Path $work 'priconfig.xml'
    & $makepri.FullName createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
    & $makepri.FullName new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
    if ($LASTEXITCODE) { throw "makepri failed" }

    $msix = "dist\Susharka-$msixVersion-x64.msix"
    & $makeappx.FullName pack /d $layout /p $msix /o | Out-Null
    if ($LASTEXITCODE) { throw "makeappx failed" }
    if (-not $isStore) { Rename-Item $msix ("Susharka-$msixVersion-x64-LOCALTEST.msix") }

    if ($RegisterMsix) {
        Get-AppxPackage -Name $identity.IdentityName | Remove-AppxPackage
        Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
        "Registered $($identity.IdentityName) from $layout; launch Susharka from Start."
    }
}

Get-ChildItem dist -File | ForEach-Object { "{0,-40} {1,6:N1} MB" -f $_.Name, ($_.Length / 1MB) }
