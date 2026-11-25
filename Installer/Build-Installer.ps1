# ══════════════════════════════════════════════════════════════════════════
#  BUILD & PACKAGING AUTOMATION SCRIPT FOR JAMESOPTIMIZER V3
# ══════════════════════════════════════════════════════════════════════════

[CmdletBinding()]
param (
    [switch]$SkipCompileInstaller,
    [string]$CertThumbprint = $env:JO_CERT_THUMBPRINT,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$AllowUnsigned,
    [switch]$TestCert,
    [switch]$UseMachineStore
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."

function Find-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
    if ($cmd) { return $cmd }
    $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $kitsRoot) {
        return Get-ChildItem -Path $kitsRoot -Filter signtool.exe -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    return $null
}

function Invoke-CodeSign {
    param(
        [string]$SignToolPath,
        [string]$Thumbprint,
        [string]$TsUrl,
        [string]$TargetFile,
        [switch]$MachineStore,
        [switch]$AllowTestStatus
    )
    if (-not (Test-Path $TargetFile)) { throw "Sign target missing: $TargetFile" }
    $signArgs = @("sign", "/sha1", $Thumbprint)
    if ($MachineStore) { $signArgs += "/sm" }
    $signArgs += @("/fd", "sha256")
    if (-not [string]::IsNullOrWhiteSpace($TsUrl)) {
        $signArgs += @("/tr", $TsUrl, "/td", "sha256")
    }
    $signArgs += $TargetFile

    & $SignToolPath @signArgs
    if ($LASTEXITCODE -ne 0) { throw "signtool failed ($LASTEXITCODE) on $TargetFile" }

    $sig = Get-AuthenticodeSignature -FilePath $TargetFile
    $subjectOk = $null -ne $sig.SignerCertificate -and $sig.SignerCertificate.Subject -like '*James God*'
    if ($AllowTestStatus) {
        if (($sig.Status -notin @('Valid', 'UnknownError', 'NotTrusted')) -or -not $subjectOk) {
            throw "Test-cert signature check failed on '$TargetFile': Status=$($sig.Status), Subject=$($sig.SignerCertificate.Subject)"
        }
    } else {
        if ($sig.Status -ne 'Valid' -or -not $subjectOk) {
            throw "Authenticode signature check failed on '$TargetFile': Status=$($sig.Status), Subject=$($sig.SignerCertificate.Subject)"
        }
    }
    Write-Host "  Signed: $TargetFile (Status=$($sig.Status), Subject=$($sig.SignerCertificate.Subject))" -ForegroundColor Green
}

if ($TestCert) {
    Write-Warning "===================================================================="
    Write-Warning " TEST-CERT MODE ACTIVE: THIS BUILD IS FOR LOCAL TESTING ONLY!       "
    Write-Warning " Untrusted/self-signed signatures are accepted in this run.         "
    Write-Warning "===================================================================="
}

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

$signTool = $null
if (-not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
    $signTool = Find-SignTool
    if (-not $signTool) { throw "signtool.exe not found in PATH or Windows Kits 10 SDK." }
    Write-Host "`n[2.5/3] Signing application executables with thumbprint $CertThumbprint..." -ForegroundColor Yellow
    Invoke-CodeSign -SignToolPath $signTool -Thumbprint $CertThumbprint -TsUrl $TimestampUrl -TargetFile (Join-Path $servicePublish "JamesOptimizer.Service.exe") -MachineStore:$UseMachineStore -AllowTestStatus:$TestCert
    Invoke-CodeSign -SignToolPath $signTool -Thumbprint $CertThumbprint -TsUrl $TimestampUrl -TargetFile (Join-Path $winuiPublish "JamesOptimizer.WinUI.exe") -MachineStore:$UseMachineStore -AllowTestStatus:$TestCert
} elseif (-not $AllowUnsigned) {
    throw "Release IPC requires Authenticode-signed JamesOptimizer.WinUI.exe ('James God'). Pass -CertThumbprint <sha1> or -AllowUnsigned."
} else {
    Write-Warning "Building unsigned binaries (-AllowUnsigned). Release pipe IPC client verification will reject unsigned JamesOptimizer.WinUI.exe."
}

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
        $setupExe = Join-Path $PSScriptRoot "Output\JamesOptimizer_V3_Setup.exe"
        if ($signTool -and -not [string]::IsNullOrWhiteSpace($CertThumbprint)) {
            Invoke-CodeSign -SignToolPath $signTool -Thumbprint $CertThumbprint -TsUrl $TimestampUrl -TargetFile $setupExe -MachineStore:$UseMachineStore -AllowTestStatus:$TestCert
        }
        Write-Host "`n🎉 Setup installer generated at: $setupExe" -ForegroundColor Green
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
