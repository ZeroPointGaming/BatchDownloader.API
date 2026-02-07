# Batch Downloader - Build Standalone Agent Script
# This script builds ONLY the .NET API as a self-contained executable.

Write-Host "Starting Batch Downloader Agent Build Process..." -ForegroundColor Cyan

$outputPath = "./client/publish/agent"

# 1. Clean up old publish files
if (Test-Path $outputPath) {
    Write-Host "Cleaning old agent publish folder..."
    Remove-Item -Recurse -Force $outputPath
}

# 2. Build the Native Windows Backend
Write-Host "Building .NET Agent (win-x64)..." -ForegroundColor Yellow
# Flags explained:
# - SelfContained: Includes .NET runtime (user doesn't need to install anything)
# - PublishSingleFile: Bundles everything into the EXE
# - PublishTrimmed: DISABLED (Caused runtime crash with Authorization middleware)
# - PublishReadyToRun: Improves startup time
# - EnableCompressionInSingleFile: Minimizes EXE size
dotnet publish ./BatchDownloader.API.csproj -c Release -r win-x64 --self-contained true -o $outputPath `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishTrimmed=false `
    /p:PublishReadyToRun=true `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=None `
    /p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    Write-Host ".NET Build Failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 3. Clean up unnecessary files
Write-Host "Cleaning up deployment folder..." -ForegroundColor Yellow
Get-ChildItem $outputPath -Include "*.pdb", "appsettings.Development.json" -Recurse | Remove-Item -Force


Write-Host "Agent Build Complete! Check the '$outputPath' folder for 'BatchDownloader.API.exe'." -ForegroundColor Green
