#Requires -Version 5.1
<#
.SYNOPSIS
  Publish AQC-Core (self-contained win-x64, unpackaged) and build Setup.exe with Inno Setup 6.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File BBSDesktop\installer\build-installer.ps1

.EXAMPLE
  powershell -File build-installer.ps1 -AppVersion 1.0.1 -SkipEngineBuild
#>
[CmdletBinding()]
param(
    [string] $AppVersion = "",
    [switch] $SkipEngineBuild,
    [switch] $SkipPublish,
    [string] $OfficeDllDir = ""
)

$ErrorActionPreference = "Stop"

$InstallerDir = $PSScriptRoot
$DesktopDir = Split-Path $InstallerDir -Parent
$AppProj = Join-Path $DesktopDir "BBSApp\BBSApp.csproj"
$EngineDll = Join-Path $DesktopDir "build\bbs_engine.dll"
$PublishDir = Join-Path $DesktopDir "artifacts\publish"
$InstallerOutDir = Join-Path $DesktopDir "artifacts\installer"
$IssPath = Join-Path $InstallerDir "O-CoreOp.iss"
$LicensePath = Join-Path (Split-Path $DesktopDir -Parent) "LICENSE"

function Get-AppVersionFromCsproj {
    param([string] $Csproj)
    [xml] $xml = Get-Content -LiteralPath $Csproj -Raw
    $ver = $xml.SelectSingleNode("//Version")
    if ($ver -and $ver.InnerText.Trim()) { return $ver.InnerText.Trim() }
    return "1.0.0"
}

function Find-ISCC {
    $candidates = [System.Collections.Generic.List[string]]::new()
    $fromPath = Get-Command ISCC -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
    if ($fromPath) { [void]$candidates.Add($fromPath) }
    foreach ($p in @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
            "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
        )) {
        if ($p -and (Test-Path -LiteralPath $p)) { [void]$candidates.Add($p) }
    }
    if ($candidates.Count -gt 0) { return $candidates[0] }
    return $null
}

if (-not $AppVersion) {
    $AppVersion = Get-AppVersionFromCsproj -Csproj $AppProj
}

Write-Host "=== AQC-Core Setup.exe installer ===" -ForegroundColor Cyan
Write-Host "Version : $AppVersion"
Write-Host "Publish : $PublishDir"
Write-Host ""

if (-not (Test-Path -LiteralPath $LicensePath)) {
    throw "LICENSE file missing at $LicensePath"
}

# --- Engine DLL ---
if (-not (Test-Path -LiteralPath $EngineDll)) {
    if ($SkipEngineBuild) {
        throw "bbs_engine.dll not found at '$EngineDll'. Build the engine or omit -SkipEngineBuild."
    }
    $cmake = $null
    foreach ($c in @(
            "cmake",
            "${env:ProgramFiles}\CMake\bin\cmake.exe",
            "${env:ProgramFiles(x86)}\CMake\bin\cmake.exe",
            "${env:LOCALAPPDATA}\Programs\CMake\bin\cmake.exe"
        )) {
        if ($c -eq "cmake") {
            $cmd = Get-Command cmake -ErrorAction SilentlyContinue
            if ($cmd) { $cmake = $cmd.Source; break }
        } elseif (Test-Path -LiteralPath $c) { $cmake = $c; break }
    }
    if (-not $cmake) { throw "cmake not found and bbs_engine.dll missing." }
    Write-Host "Building bbs_engine.dll..." -ForegroundColor Yellow
    $buildDir = Join-Path $DesktopDir "build"
    if (-not (Test-Path -LiteralPath (Join-Path $buildDir "CMakeCache.txt"))) {
        & $cmake -S $DesktopDir -B $buildDir -G Ninja
        if ($LASTEXITCODE -ne 0) { throw "cmake configure failed ($LASTEXITCODE)" }
    }
    & $cmake --build $buildDir --config Release --target bbs_engine
    if ($LASTEXITCODE -ne 0) { throw "cmake build bbs_engine failed ($LASTEXITCODE)" }
} else {
    Write-Host "Engine  : $EngineDll (ok)"
}

# --- Publish unpackaged (override MSIX for classic installer payload) ---
if (-not $SkipPublish) {
    Write-Host "Publishing self-contained win-x64 (unpackaged)..." -ForegroundColor Yellow
    if (Test-Path -LiteralPath $PublishDir) {
        Remove-Item -LiteralPath $PublishDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

    & dotnet publish $AppProj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:Platform=x64 `
        -p:WindowsPackageType=None `
        -p:PublishSingleFile=false `
        -p:GenerateAppxPackageOnBuild=false `
        -p:AppxPackageSigningEnabled=false `
        "-p:OfficeDllDir=$OfficeDllDir" `
        -o $PublishDir

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

$exe = Join-Path $PublishDir "O-CoreOp.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Publish output missing O-CoreOp.exe at $PublishDir"
}

$pubEngine = Join-Path $PublishDir "bbs_engine.dll"
if (-not (Test-Path -LiteralPath $pubEngine)) {
    Write-Host "Copying bbs_engine.dll into publish folder..." -ForegroundColor Yellow
    Copy-Item -LiteralPath $EngineDll -Destination $pubEngine -Force
}

# office.dll (the shared company-file engine) comes from the O-STUDIO build; without it the installed app cannot open a company file.
if (-not (Test-Path -LiteralPath (Join-Path $PublishDir "office.dll"))) {
    throw "office.dll is missing from the publish folder. Build office-ffi in the O-STUDIO repository (release) and pass -OfficeDllDir <folder with office.dll>."
}

Write-Host "Publish : O-CoreOp.exe + bbs_engine.dll + office.dll ready"

# --- ShilpiDB engine (bundled sibling process; not a separate product) ---
function Find-ShilpiEngineBins {
    $names = @('shilpid.exe', 'shilpi-http.exe')
    $candidates = [System.Collections.Generic.List[string]]::new()
    if ($env:SHILPIDB_BIN) { [void]$candidates.Add($env:SHILPIDB_BIN) }
    $repoRoot = Split-Path $DesktopDir -Parent
    $shilpi = Join-Path $repoRoot '..\shilpidb'
    if (Test-Path $shilpi) {
        $shilpi = (Resolve-Path $shilpi).Path
        [void]$candidates.Add((Join-Path $shilpi 'target\release'))
        [void]$candidates.Add((Join-Path $shilpi 'target\x86_64-pc-windows-msvc\release'))
    }
    foreach ($suite in @(
            (Join-Path $repoRoot '..\OSAS-Open_Source_Architecture_Suit\installer\suite-tools'),
            (Join-Path $env:LOCALAPPDATA 'Programs\OSAS\shilpidb')
        )) {
        if (Test-Path $suite) { [void]$candidates.Add((Resolve-Path $suite).Path) }
    }
    foreach ($dir in $candidates) {
        if (($names | ForEach-Object { Test-Path (Join-Path $dir $_) }) -notcontains $false) { return $dir }
    }
    if (Test-Path (Join-Path $repoRoot '..\shilpidb\Cargo.toml')) {
        $repo = (Resolve-Path (Join-Path $repoRoot '..\shilpidb')).Path
        Write-Host "Building shilpid + shilpi-http from $repo ..." -ForegroundColor Yellow
        Push-Location $repo
        try {
            cargo build --release -p shilpid -p shilpi-http
            if ($LASTEXITCODE -ne 0) { throw "shilpidb engine build failed ($LASTEXITCODE)" }
        } finally { Pop-Location }
        $built = Join-Path $repo 'target\release'
        if (($names | ForEach-Object { Test-Path (Join-Path $built $_) }) -notcontains $false) { return $built }
    }
    return $null
}
$shilpiSrc = Find-ShilpiEngineBins
if ($shilpiSrc) {
    Copy-Item (Join-Path $shilpiSrc 'shilpid.exe') $PublishDir -Force
    Copy-Item (Join-Path $shilpiSrc 'shilpi-http.exe') $PublishDir -Force
    Write-Host "ShilpiDB engine : bundled from $shilpiSrc"
} else {
    Write-Warning "shilpid.exe / shilpi-http.exe not found — installer will not include the ShilpiDB engine. Set SHILPIDB_BIN or build ..\shilpidb."
}

# --- Inno Setup ---
$iscc = Find-ISCC
if (-not $iscc) {
    throw @"
Inno Setup 6 not found (ISCC.exe).
Install from https://jrsoftware.org/isdl.php then re-run this script.
"@
}

Write-Host "Compiling installer with $iscc ..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $InstallerOutDir -Force | Out-Null

& $iscc "/DAppVersion=$AppVersion" $IssPath
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Join-Path $InstallerOutDir "O-CoreOp-Setup-$AppVersion.exe"
if (-not (Test-Path -LiteralPath $setup)) {
    throw "Expected installer not found: $setup"
}

Write-Host ""
Write-Host "Installer ready:" -ForegroundColor Green
Write-Host "  $setup"
Write-Host "  Installs to %LocalAppData%\Programs\O-CoreOp\"
Write-Host ""
Get-Item $setup | Format-List FullName, Length, LastWriteTime
