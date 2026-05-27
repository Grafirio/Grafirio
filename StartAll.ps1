Write-Host "GRAFIRIO - Tüm Servisleri Başlat" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# .NET Servisleri
Write-Host "1. .NET Servisleri başlatılıyor..." -ForegroundColor Yellow

Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd 'd:\Projeler\Grifirio\src\services\Grafirio.Identity.Api\Grafirio.Identity.Api'; dotnet run"
Start-Sleep -Seconds 3

Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd 'd:\Projeler\Grifirio\src\services\Grafirio.Catalog.Api'; dotnet run"
Start-Sleep -Seconds 3

Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd 'd:\Projeler\Grifirio\src\services\Grafirio.Basket.Api'; dotnet run"
Start-Sleep -Seconds 3

Write-Host "  ✓ .NET Servisleri başlatıldı" -ForegroundColor Green
Write-Host ""

# Frontend
Write-Host "2. Frontend başlatılıyor..." -ForegroundColor Yellow
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd 'd:\Projeler\Grifirio\src\front\grifirio.front'; npm run dev"
Write-Host "  ✓ Frontend başlatıldı" -ForegroundColor Green
Write-Host ""

# Docker servisleri
Write-Host "3. Docker servisleri kontrol ediliyor..." -ForegroundColor Yellow
$dockerOk = $false
docker info 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Docker çalışıyor" -ForegroundColor Green
    Write-Host "  → AI servisleri başlatılıyor..." -ForegroundColor Yellow
    
    cd "d:\Projeler\Grifirio"
    docker-compose up -d mongo.db.identity mongo.db.catalog mongo.db.discount sqlserver.db.order postgres.db.keycloak postgres.db.dataanalysis rabbitmq redis.db.basket schema.analyzer pycaret.engine django.ai celery.worker
    
    Write-Host "  ✓ AI servisleri başlatıldı" -ForegroundColor Green
} else {
    Write-Host "  ⚠ Docker çalışmıyor - AI servisleri atlandı" -ForegroundColor Yellow
    Write-Host "  → Docker'ı manuel başlatın: Docker Desktop" -ForegroundColor Gray
}
Write-Host ""

# Özet
Write-Host "=====================================" -ForegroundColor Green
Write-Host "TÜM SERVİSLER BAŞLATILDI!" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Green
Write-Host ""
Write-Host "URL'ler:" -ForegroundColor Cyan
Write-Host "  Test Sayfası:  http://localhost:59264/test-ai-services.html" -ForegroundColor White
Write-Host "  AI Dashboard:  http://localhost:59264/ai-dashboard-public" -ForegroundColor White
Write-Host "  Identity API:  http://localhost:5036/swagger" -ForegroundColor White
Write-Host "  Catalog API:   http://localhost:5280/swagger" -ForegroundColor White
Write-Host ""

Start-Sleep -Seconds 10
Start-Process "http://localhost:59264/test-ai-services.html"

Write-Host "Bu pencereyi kapatabilirsiniz." -ForegroundColor Gray
