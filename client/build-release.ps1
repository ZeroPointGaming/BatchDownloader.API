# Batch Downloader - Build Release Script

Write-Host "🚀 Starting Batch Downloader Build Process..." -ForegroundColor Cyan

# 1. Clean up old publish files
if (Test-Path "./publish") {
    Write-Host "🧹 Cleaning old publish folder..."
    Remove-Item -Recurse -Force "./publish"
}

# 2. Build the Native Windows Backend
Write-Host "📦 Building .NET Backend (win-x64)..." -ForegroundColor Yellow
dotnet publish ../BatchDownloader.API.csproj -c Release -r win-x64 --self-contained true -o ./publish

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ .NET Build Failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 3. Build the React Frontend
Write-Host "⚛️ Building React Frontend..." -ForegroundColor Yellow
npm run build

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Frontend Build Failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 4. Package everything with Electron Builder
Write-Host "🎁 Packaging Portable Electron App..." -ForegroundColor Yellow
npm run dist

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Electron Packaging Failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "🎉 Build Complete! Check the 'release' folder for your app." -ForegroundColor Green
