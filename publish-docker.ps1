# Batch Downloader - Publish Docker Image Script
# This script builds the backend Docker image and pushes it to a registry.

param (
    [string]$ImageName = "batch-downloader-api",
    [string]$Registry = "docker.io", # Default to Docker Hub
    [string]$Tag = "latest",
    [string]$Username = "" # Your Docker Hub or Registry username
)

if (-not $Username) {
    Write-Host "Error: Username is required." -ForegroundColor Red
    Write-Host "Usage: ./publish-docker.ps1 -Username yourusername"
    exit 1
}

$FullImageName = "$Registry/$Username/$ImageName"
$TaggedImageName = "${FullImageName}:${Tag}"

Write-Host "Starting Docker Publish Process for $TaggedImageName..." -ForegroundColor Cyan

# 1. Build the Docker Image
Write-Host "Building Docker Image..." -ForegroundColor Yellow
docker build -t $ImageName .

if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 2. Tag the Image
Write-Host "Tagging Image as $TaggedImageName..." -ForegroundColor Yellow
docker tag $ImageName $TaggedImageName

# 3. Push the Image
Write-Host "Pushing Image to $Registry..." -ForegroundColor Yellow
docker push $TaggedImageName

if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker push failed! Make sure you are logged in (docker login)." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "🎉 Successfully published $TaggedImageName!" -ForegroundColor Green
