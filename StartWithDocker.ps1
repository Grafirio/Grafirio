# Grafirio - Complete Docker Startup Script
# This script starts ALL services (Infrastructure + AI + .NET APIs + Frontend) using Docker

Write-Host "=================================" -ForegroundColor Cyan
Write-Host "   Grafirio Docker Startup" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

# Check if Docker is running
Write-Host "[1/3] Checking Docker status..." -ForegroundColor Yellow
$dockerRunning = docker info 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Docker is not running. Please start Docker Desktop first." -ForegroundColor Red
    exit 1
}
Write-Host "✓ Docker is running" -ForegroundColor Green
Write-Host ""

# Stop and remove existing containers
Write-Host "[2/3] Cleaning up existing containers..." -ForegroundColor Yellow
docker-compose down 2>&1 | Out-Null
Write-Host "✓ Cleanup completed" -ForegroundColor Green
Write-Host ""

# Start all services
Write-Host "[3/3] Starting ALL services with docker-compose..." -ForegroundColor Yellow
Write-Host "     This may take 5-10 minutes for first time (building .NET images)..." -ForegroundColor Gray
Write-Host ""
docker-compose up -d --build

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "=================================" -ForegroundColor Green
    Write-Host "   ALL SERVICES STARTED!" -ForegroundColor Green
    Write-Host "=================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Infrastructure Services:" -ForegroundColor Cyan
    Write-Host "  - RabbitMQ Management UI: http://localhost:15672" -ForegroundColor White
    Write-Host "  - MongoDB UI: http://localhost:27032" -ForegroundColor White
    Write-Host "  - Redis UI: http://localhost:27033" -ForegroundColor White
    Write-Host "  - PostgreSQL UI: http://localhost:8888" -ForegroundColor White
    Write-Host ""
    Write-Host ".NET Microservices:" -ForegroundColor Cyan
    Write-Host "  - Identity API: http://localhost:5036" -ForegroundColor White
    Write-Host "  - Catalog API: http://localhost:5280" -ForegroundColor White
    Write-Host "  - Basket API: http://localhost:5023" -ForegroundColor White
    Write-Host "  - Data Analysis API: http://localhost:5221" -ForegroundColor White
    Write-Host "  - Gateway (YARP): http://localhost:5000" -ForegroundColor White
    Write-Host ""
    Write-Host "AI Services:" -ForegroundColor Cyan
    Write-Host "  - Django AI Service: http://localhost:8000" -ForegroundColor White
    Write-Host "  - Schema Analyzer: http://localhost:8001" -ForegroundColor White
    Write-Host "  - PyCaret Engine: http://localhost:8002" -ForegroundColor White
    Write-Host ""
    Write-Host "Useful Commands:" -ForegroundColor Yellow
    Write-Host "  - View logs: docker-compose logs -f [service-name]" -ForegroundColor Gray
    Write-Host "  - Stop all: docker-compose down" -ForegroundColor Gray
    Write-Host "  - Restart service: docker-compose restart [service-name]" -ForegroundColor Gray
    Write-Host "  - View status: docker-compose ps" -ForegroundColor Gray
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "ERROR: Failed to start services. Check logs with: docker-compose logs" -ForegroundColor Red
    exit 1
}
