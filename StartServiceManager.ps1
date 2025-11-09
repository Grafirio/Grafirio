# Grafirio Service Manager Başlatma Scripti
# Kullanım: .\StartServiceManager.ps1

Write-Host "🎛️ Grafirio Service Manager + API başlatılıyor..." -ForegroundColor Cyan
Write-Host ""

# Proje dizinleri
$projectPath = "d:\Projeler\Grifirio\src\front\admins\grifirio.projectadmin"
$apiPath = "d:\Projeler\Grifirio\src\front\admins\grifirio.projectadmin\api"

if (-Not (Test-Path $projectPath)) {
    Write-Host "❌ Hata: Proje dizini bulunamadı: $projectPath" -ForegroundColor Red
    exit 1
}

if (-Not (Test-Path $apiPath)) {
    Write-Host "❌ Hata: API dizini bulunamadı: $apiPath" -ForegroundColor Red
    exit 1
}

Write-Host "📂 Proje dizini: $projectPath" -ForegroundColor Yellow
Set-Location $projectPath

# node_modules kontrolü
if (-Not (Test-Path "node_modules")) {
    Write-Host "📦 node_modules bulunamadı. npm install çalıştırılıyor..." -ForegroundColor Yellow
    npm install
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ npm install başarısız!" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "✅ Bağımlılıklar hazır" -ForegroundColor Green
Write-Host ""

# API bağımlılıklarını kontrol et
Set-Location $apiPath
if (-Not (Test-Path "node_modules")) {
    Write-Host "� API dependencies yükleniyor..." -ForegroundColor Yellow
    npm install
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
Write-Host "   🎛️  GRAFIRIO SERVICE MANAGER" -ForegroundColor Magenta
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
Write-Host ""
Write-Host "   📊 15 Servis | 4 Kategori | Gerçek Zamanlı İzleme" -ForegroundColor White
Write-Host "   🐳 Docker Kontrol | ▶️ Başlat | ⏹️ Durdur | 🔄 Restart" -ForegroundColor White
Write-Host ""
Write-Host "   🔗 Dashboard: http://localhost:59666" -ForegroundColor Green
Write-Host "   🔗 API: http://localhost:3001" -ForegroundColor Green
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
Write-Host ""

# API'yi ayrı terminal'de başlat
Write-Host "� API başlatılıyor (Port 3001)..." -ForegroundColor Yellow
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$apiPath'; Write-Host '🎛️ Service Manager API' -ForegroundColor Cyan; Write-Host 'Port: 3001' -ForegroundColor Green; Write-Host ''; node server.js" -WindowStyle Normal

Start-Sleep -Seconds 2

# Dashboard'u ayrı terminal'de başlat  
Write-Host "🎨 Dashboard başlatılıyor (Port 59666)..." -ForegroundColor Yellow
Set-Location $projectPath
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$projectPath'; Write-Host '🎛️ Service Manager Dashboard' -ForegroundColor Cyan; Write-Host 'Port: 59666' -ForegroundColor Green; Write-Host ''; npm run dev" -WindowStyle Normal

Start-Sleep -Seconds 5

# Browser'ı aç
Write-Host ""
Write-Host "✅ Tüm servisler başlatıldı!" -ForegroundColor Green
Write-Host ""
Write-Host "💡 Dashboard tarayıcıda açılıyor..." -ForegroundColor Yellow
Start-Process "http://localhost:59666"

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
Write-Host "   ✅ Service Manager HAZIR" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
Write-Host ""

exit 0

# Hata durumunda
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "❌ Service Manager başlatılamadı!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Sorun giderme adımları:" -ForegroundColor Yellow
    Write-Host "  1. npm install çalıştırın" -ForegroundColor White
    Write-Host "  2. package.json dosyasını kontrol edin" -ForegroundColor White
    Write-Host "  3. Port 59666'nın kullanılmadığından emin olun" -ForegroundColor White
    Write-Host ""
    exit 1
}
