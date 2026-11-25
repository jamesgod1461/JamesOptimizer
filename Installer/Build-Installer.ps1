# ══════════════════════════════════════════════════════════════════════════
#  BUILD & PACKAGING AUTOMATION SCRIPT FOR JAMESOPTIMIZER V3
# ══════════════════════════════════════════════════════════════════════════

[CmdletBinding()]
param (
    [switch]$SkipCompileInstaller
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  BUILDING JAMESOPTIMIZER V3 (Full Production Pipeline)   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$servicePublish = Join-Path $root "JamesOptimizer.Service\bin\Release\net10.0\win-x64\publish"
$winuiPublish   = Join-Path $root "JamesOptimizer.WinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"

# Clean stale publish folders so removed/old DLLs never leak into the installer
foreach ($dir in @($servicePublish, $winuiPublish)) {
    if (Test-Path $dir) {
        Remove-Item -Path $dir -Recurse -Force
    }
}

# 1. Publish JamesOptimizer.Service (Background Service - Single-File Self-Contained Win-x64)
Write-Host "`n[1/3] Publishing JamesOptimizer.Service..." -ForegroundColor Yellow
$serviceProj = Join-Path $root "JamesOptimizer.Service\JamesOptimizer.Service.csproj"
dotnet publish $serviceProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish JamesOptimizer.Service"
    exit $LASTEXITCODE
}
Get-ChildItem -Path $servicePublish -Filter *.pdb -Recurse -File | Remove-Item -Force -ErrorAction SilentlyContinue
$svcStats = Get-ChildItem -Path $servicePublish -Recurse -File
Write-Host ("✅ JamesOptimizer.Service published: {0} file(s), {1:N1} MB" -f $svcStats.Count, (($svcStats | Measure-Object Length -Sum).Sum / 1MB)) -ForegroundColor Green

# 2. Publish JamesOptimizer.WinUI (Modern WinUI 3 Client)
Write-Host "`n[2/3] Publishing JamesOptimizer.WinUI..." -ForegroundColor Yellow
$winuiProj = Join-Path $root "JamesOptimizer.WinUI\JamesOptimizer.WinUI.csproj"
dotnet publish $winuiProj -c Release -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish JamesOptimizer.WinUI"
    exit $LASTEXITCODE
}

# Strip debug symbols, crash dumper, and non-English Windows App SDK .mui locale folders
Get-ChildItem -Path $winuiPublish -Include *.pdb,createdump.exe -Recurse -File | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $winuiPublish -Directory | Where-Object {
    $_.Name -match '^[a-z]{2,3}(-[A-Za-z0-9]+){1,2}$' -and $_.Name -notin @('en-us', 'en-US', 'en') -and
    (Get-ChildItem -Path $_.FullName -Include *.mui,*.resources.dll -Recurse -File | Select-Object -First 1)
} | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$uiStats = Get-ChildItem -Path $winuiPublish -Recurse -File
Write-Host ("✅ JamesOptimizer.WinUI published: {0} file(s), {1:N1} MB" -f $uiStats.Count, (($uiStats | Measure-Object Length -Sum).Sum / 1MB)) -ForegroundColor Green

# 3. Compile Inno Setup Script
Write-Host "`n[3/3] Compiling Installer..." -ForegroundColor Yellow
$issPath = Join-Path $PSScriptRoot "JamesOptimizerSetup.iss"

$iscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc -and -not $SkipCompileInstaller) {
    Write-Host "Found Inno Setup Compiler at: $iscc" -ForegroundColor Cyan
    & $iscc $issPath
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n🎉 Setup installer generated at: $PSScriptRoot\Output\JamesOptimizer_V3_Setup.exe" -ForegroundColor Green
    } else {
        Write-Warning "Inno Setup compilation exited with error code $LASTEXITCODE"
    }
} else {
    Write-Host "Inno Setup (ISCC.exe) not found on system path." -ForegroundColor Yellow
    Write-Host "Published binaries are staged and ready in:" -ForegroundColor Cyan
    Write-Host " - $root\JamesOptimizer.WinUI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish" -ForegroundColor White
    Write-Host " - $root\JamesOptimizer.Service\bin\Release\net10.0\win-x64\publish" -ForegroundColor White
    Write-Host "You can compile '$issPath' whenever Inno Setup is installed." -ForegroundColor Cyan
}

Write-Host "`nBuild pipeline complete!" -ForegroundColor Green
