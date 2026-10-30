# ══════════════════════════════════════════════════════════════════════════
#  BUILD & PACKAGING AUTOMATION SCRIPT FOR JAMES OPTIMIZER PRO
# ══════════════════════════════════════════════════════════════════════════

[CmdletBinding()]
param (
    [switch]$SkipCompileInstaller
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  BUILDING JAMES OPTIMIZER PRO (WinUI + Service Pipeline)  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Publish JamesOptimizer.WinUI (Self-Contained Unpackaged Win-x64)
Write-Host "`n[1/3] Publishing JamesOptimizer.WinUI..." -ForegroundColor Yellow
$winuiProj = Join-Path $root "JamesOptimizer.WinUI\JamesOptimizer.WinUI.csproj"
dotnet publish $winuiProj -c Release -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish JamesOptimizer.WinUI"
    exit $LASTEXITCODE
}
Write-Host "✅ JamesOptimizer.WinUI published successfully!" -ForegroundColor Green

# 2. Publish JamesOptimizer.Service (Self-Contained Win-x64)
Write-Host "`n[2/3] Publishing JamesOptimizer.Service..." -ForegroundColor Yellow
$serviceProj = Join-Path $root "JamesOptimizer.Service\JamesOptimizer.Service.csproj"
dotnet publish $serviceProj -c Release -r win-x64 --self-contained true

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish JamesOptimizer.Service"
    exit $LASTEXITCODE
}
Write-Host "✅ JamesOptimizer.Service published successfully!" -ForegroundColor Green

# 3. Compile Inno Setup Script
Write-Host "`n[3/3] Compiling Installer..." -ForegroundColor Yellow
$issPath = Join-Path $PSScriptRoot "JamesOptimizerSetup.iss"

$innoPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_) }

if ($innoPaths -and -not $SkipCompileInstaller) {
    $iscc = $innoPaths[0]
    Write-Host "Found Inno Setup Compiler at: $iscc" -ForegroundColor Cyan
    & $iscc $issPath
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n🎉 Setup installer generated at: $PSScriptRoot\Output\JamesOptimizerSetup_v3.0.0.exe" -ForegroundColor Green
    } else {
        Write-Warning "Inno Setup compilation exited with error code $LASTEXITCODE"
    }
} else {
    Write-Host "Inno Setup (ISCC.exe) not found on system path." -ForegroundColor Yellow
    Write-Host "Published binaries are staged and ready in:" -ForegroundColor Cyan
    Write-Host " - $root\JamesOptimizer.WinUI\bin\Release\net8.0-windows10.0.26100.0\win-x64\publish" -ForegroundColor White
    Write-Host " - $root\JamesOptimizer.Service\bin\Release\net10.0\win-x64\publish" -ForegroundColor White
    Write-Host "You can compile '$issPath' whenever Inno Setup is installed." -ForegroundColor Cyan
}

Write-Host "`nBuild pipeline complete!" -ForegroundColor Green
