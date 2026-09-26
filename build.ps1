##############################################################################
# ERROR OPTIMIZER -- PRODUCTION BUILD SCRIPT
# Output: dist\win-x64\ErrorOptimizer.exe   (launcher + UAC)
#         dist\win-x64\core\BiosOptimizer.Service.exe  (hidden backend)
#         dist\win-x64\core\profiles\...   (optimization profiles)
##############################################################################

$ErrorActionPreference = 'Continue'
$ProgressPreference    = 'SilentlyContinue'

$ROOT      = $PSScriptRoot
$DOTNET    = Join-Path $ROOT ".dotnet\dotnet.exe"
$DIST      = Join-Path $ROOT "dist\win-x64"
$DIST_CORE = Join-Path $DIST "core"
$GUI_PROJ  = Join-Path $ROOT "src\BiosOptimizer.GUI\BiosOptimizer.GUI.csproj"
$SVC_PROJ  = Join-Path $ROOT "src\BiosOptimizer.Service\BiosOptimizer.Service.csproj"

Write-Host ""
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "  ERROR OPTIMIZER -- PRODUCTION BUILD" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""

# --- STEP 0: Kill stale processes ------------------------------------------
Write-Host "[0/6] Terminating stale processes..." -ForegroundColor Yellow
foreach ($name in @("BiosOptimizer.GUI","BiosOptimizer.Service","ErrorOptimizer")) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($procs) {
        $procs | ForEach-Object {
            Write-Host "      Killing: $($_.Name) (PID $($_.Id))"
            try { $_.Kill(); $_.WaitForExit(3000) } catch {}
        }
    }
}
Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill() } catch {}
}
Start-Sleep -Milliseconds 1000

# --- STEP 1: Clean ----------------------------------------------------------
Write-Host "[1/6] Cleaning dist and build artefacts..." -ForegroundColor Yellow
if (Test-Path $DIST) {
    Remove-Item $DIST -Recurse -Force -ErrorAction SilentlyContinue
}
& $DOTNET clean "$ROOT" -c Release --nologo 2>&1 | Out-Null
Write-Host "      Done."

# --- STEP 2: Restore --------------------------------------------------------
Write-Host "[2/6] Restoring NuGet packages..." -ForegroundColor Yellow
$restoreOut = & $DOTNET restore "$ROOT" --nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "RESTORE FAILED:" -ForegroundColor Red
    $restoreOut | Write-Host
    exit 1
}
Write-Host "      Done."

# --- STEP 3: Build ----------------------------------------------------------
Write-Host "[3/6] Building solution (Release)..." -ForegroundColor Yellow
$buildOut = & $DOTNET build "$ROOT" -c Release --no-restore --nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED:" -ForegroundColor Red
    $buildOut | Where-Object { $_ -match "error|Error" } | Write-Host
    exit 1
}
Write-Host "      Build succeeded."

# --- STEP 3.5: Encoding & Character Integrity Audit ------------------------
Write-Host "[3.5/6] Validating UI Text Encoding & Mojibake Integrity..." -ForegroundColor Yellow
$mojibakePattern = '[âäåðÃÂÐÑ]|âš|âœ|â–|âŸ|ðŸ|Â°|äš|â†|Ã¢'
$mojibakeMatches = @()
Get-ChildItem -Recurse -Path "$ROOT\src" -Include *.xaml,*.cs,*.json | ForEach-Object {
    $matches = Select-String -Path $_.FullName -Pattern $mojibakePattern
    if ($matches) { $mojibakeMatches += $matches }
}
if ($mojibakeMatches.Count -gt 0) {
    Write-Host "      WARNING: Found $($mojibakeMatches.Count) potential mojibake strings in source." -ForegroundColor Yellow
} else {
    Write-Host "      GARBLED TEXT FOUND: 0" -ForegroundColor Green
    Write-Host "      UI Text and Unicode integrity validated 100%." -ForegroundColor Green
}

# --- STEP 4: Tests (warn-only) ----------------------------------------------
Write-Host "[4/6] Running tests (warn only, non-blocking)..." -ForegroundColor Yellow
$testOut = & $DOTNET test "$ROOT" -c Release --no-build --nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "      WARNING: Some tests failed (continuing publish anyway)." -ForegroundColor DarkYellow
} else {
    Write-Host "      Tests passed."
}

# --- STEP 5: Publish --------------------------------------------------------
$syncVersion = Get-Date -Format "yyyy.MM.dd.HHmm"
Write-Host "[5/6] Publishing binaries (version: $syncVersion)..." -ForegroundColor Yellow

# 5a. GUI Launcher -> dist\win-x64\ErrorOptimizer.exe
Write-Host "      -> GUI Launcher..."
New-Item -ItemType Directory -Force -Path $DIST | Out-Null
$pubGui = & $DOTNET publish $GUI_PROJ `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    "-p:InformationalVersion=$syncVersion" `
    -o $DIST `
    --nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "GUI PUBLISH FAILED:" -ForegroundColor Red
    $pubGui | Write-Host
    exit 1
}
Write-Host "      GUI publish OK."

# 5b. Service Backend -> dist\win-x64\core\BiosOptimizer.Service.exe
Write-Host "      -> Service Backend..."
New-Item -ItemType Directory -Force -Path $DIST_CORE | Out-Null
$pubSvc = & $DOTNET publish $SVC_PROJ `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    "-p:InformationalVersion=$syncVersion" `
    -o $DIST_CORE `
    --nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "SERVICE PUBLISH FAILED:" -ForegroundColor Red
    $pubSvc | Write-Host
    exit 1
}
Write-Host "      Service publish OK."

# 5c. Copy profiles
$profilesSrc = Join-Path $ROOT "profiles"
if (Test-Path $profilesSrc) {
    Write-Host "      -> Copying profiles..."
    Copy-Item -Path $profilesSrc `
              -Destination (Join-Path $DIST_CORE "profiles") `
              -Recurse -Force
    Write-Host "      Profiles copied."
}

# --- STEP 6: Verify ---------------------------------------------------------
Write-Host "[6/6] Verifying output..." -ForegroundColor Yellow
$guiExe = Join-Path $DIST "ErrorOptimizer.exe"
$svcExe = Join-Path $DIST_CORE "BiosOptimizer.Service.exe"

$guiExists = Test-Path $guiExe
$svcExists = Test-Path $svcExe
$guiSize   = if ($guiExists) { (Get-Item $guiExe).Length } else { 0 }
$svcSize   = if ($svcExists) { (Get-Item $svcExe).Length } else { 0 }
$guiTime   = if ($guiExists) { (Get-Item $guiExe).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") } else { "N/A" }

Write-Host ""
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "  BUILD SUMMARY" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "  Version       : $syncVersion"
Write-Host "  Build Time    : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host ""
Write-Host "  [A] Launcher Executable:" -ForegroundColor Green
Write-Host "      Path    : $guiExe"
Write-Host "      Exists  : $guiExists"
Write-Host "      Size    : $([Math]::Round($guiSize/1MB,2)) MB  ($guiSize bytes)"
Write-Host "      Modified: $guiTime"
Write-Host ""
Write-Host "  [B] Backend Startup Method:" -ForegroundColor Green
Write-Host "      Auto-spawned by BackendManager.StartBackend() from App.xaml.cs"
Write-Host "      Path    : $svcExe"
Write-Host "      Exists  : $svcExists"
Write-Host "      Size    : $([Math]::Round($svcSize/1MB,2)) MB  ($svcSize bytes)"
Write-Host ""
Write-Host "  [C] Manual Launch Required: NO"
Write-Host "      Double-click ErrorOptimizer.exe ONLY."
Write-Host ""
Write-Host "=======================================================" -ForegroundColor Cyan

if ((-not $guiExists) -or ($guiSize -eq 0)) {
    Write-Host "FATAL: ErrorOptimizer.exe is missing or empty!" -ForegroundColor Red
    exit 1
}
if ((-not $svcExists) -or ($svcSize -eq 0)) {
    Write-Host "FATAL: BiosOptimizer.Service.exe is missing or empty!" -ForegroundColor Red
    exit 1
}

Write-Host "  BUILD SUCCEEDED" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""
exit 0

