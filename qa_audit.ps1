$ErrorActionPreference = "Continue"

Write-Host "--- PHASE 1: PROCESS CLEANUP ---"
Stop-Process -Name "BiosOptimizer.GUI" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "BiosOptimizer.Service" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "BiosOptimizer.Service.old" -Force -ErrorAction SilentlyContinue

Write-Host "--- PHASE 2: SERVICE CLEANUP ---"
$svcName = "BiosOptimizerService"
if (Get-Service $svcName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $svcName -Force -ErrorAction SilentlyContinue
    sc.exe delete $svcName
    Write-Host "Deleted existing service: $svcName"
}

Write-Host "--- PHASE 3: CLEAN BUILD ---"
cd "d:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER"
$syncVersion = Get-Date -Format "yyyy.MM.dd.HHmm"
Write-Host "Building version: $syncVersion"

dotnet clean
dotnet publish src/BiosOptimizer.Service/BiosOptimizer.Service.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:InformationalVersion=$syncVersion -o "dist\win-x64\service"
dotnet publish src/BiosOptimizer.GUI/BiosOptimizer.GUI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:InformationalVersion=$syncVersion -o "dist\win-x64\gui"

Write-Host "--- PHASE 4: BINARY AUDIT ---"
$guiPath = "d:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\dist\win-x64\gui\BiosOptimizer.GUI.exe"
$svcPath = "d:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\dist\win-x64\service\BiosOptimizer.Service.exe"

if (Test-Path $guiPath) {
    $guiHash = (Get-FileHash $guiPath -Algorithm SHA256).Hash
    Write-Host "GUI Exe: $guiPath (SHA256: $guiHash)"
} else {
    Write-Host "GUI Build Failed!"
}

if (Test-Path $svcPath) {
    $svcHash = (Get-FileHash $svcPath -Algorithm SHA256).Hash
    Write-Host "Service Exe: $svcPath (SHA256: $svcHash)"
} else {
    Write-Host "Service Build Failed!"
}

Write-Host "--- PHASE 5: RUNTIME TEST ---"
if ((Test-Path $svcPath) -and (Test-Path $guiPath)) {
    Write-Host "Starting Service as Process (non-elevated for IPC test)..."
    Start-Process -FilePath $svcPath
    Start-Sleep -Seconds 2
    Write-Host "Starting GUI..."
    Start-Process -FilePath $guiPath
    Write-Host "Test environment launched. Please check the UI."
}
