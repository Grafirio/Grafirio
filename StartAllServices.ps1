# Grafirio - Tüm Servisleri Başlat
# Bu script tüm servisleri otomatik olarak başlatır

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GRAFIRIO - Servis Başlatma Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

function Start-DetachedPowerShell {
    param(
        [string]$Title,
        [string]$WorkingDirectory,
        [string]$Command
    )

    Start-Process -FilePath powershell.exe -ArgumentList @(
        '-NoExit',
        '-Command',
        "Write-Host '$Title' -ForegroundColor Cyan; Set-Location '$WorkingDirectory'; $Command"
    ) -WindowStyle Normal | Out-Null
}

function Test-DockerReady {
    try {
        docker info 2>&1 | Out-Null
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

Write-Host "[1/6] Docker check..." -ForegroundColor Yellow
$dockerRunning = Test-DockerReady
if ($dockerRunning) {
    Write-Host "  Docker is running" -ForegroundColor Green
} else {
    Write-Host "  Docker is not running" -ForegroundColor Yellow
    Write-Host "  Starting Docker Desktop..." -ForegroundColor Yellow
    try {
        Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe" -ErrorAction Stop | Out-Null
        $deadline = (Get-Date).AddSeconds(120)
        while (-not $dockerRunning -and (Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 5
            $dockerRunning = Test-DockerReady
        }
        if ($dockerRunning) {
            Write-Host "  Docker started" -ForegroundColor Green
        }
    } catch {
        Write-Host "  Docker Desktop not found" -ForegroundColor Yellow
    }
}

# The APIs below run on the host but PostgreSQL and MongoDB live in Docker:
# starting them without the databases makes every connection-list request fail with HTTP 503.
if (-not $dockerRunning) {
    Write-Host "  Docker is required for the databases. Start Docker Desktop and run this script again." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "[2/6] Starting databases (waiting until healthy)..." -ForegroundColor Yellow
Push-Location "d:\Projeler\Grifirio"
docker compose up -d --wait postgres.db.dataanalysis mongo.db rabbitmq
$databasesReady = $LASTEXITCODE -eq 0
Pop-Location
if (-not $databasesReady) {
    Write-Host "  PostgreSQL / MongoDB did not become healthy. Check: docker compose ps, and that DATAANALYSIS_DB_PASSWORD is set in .env" -ForegroundColor Red
    exit 1
}
Write-Host "  PostgreSQL (5433), MongoDB (27017) and RabbitMQ (5672) are healthy" -ForegroundColor Green

Write-Host ""
Write-Host "[3/6] Starting .NET services..." -ForegroundColor Yellow
Start-DetachedPowerShell -Title "Identity API starting..." -WorkingDirectory "d:\Projeler\Grifirio\src\services\Grafirio.Identity.Api\Grafirio.Identity.Api" -Command "dotnet run"
Start-Sleep -Seconds 2
Start-DetachedPowerShell -Title "Commerce API starting..." -WorkingDirectory "d:\Projeler\Grifirio\src\services\Grafirio.Commerce.Api" -Command "dotnet run"
Start-Sleep -Seconds 2
Start-DetachedPowerShell -Title "Data Analysis API starting..." -WorkingDirectory "d:\Projeler\Grifirio\src\services\Grafirio.DataAnalysis.Api" -Command "dotnet run"
Start-Sleep -Seconds 2
Start-DetachedPowerShell -Title "Gateway starting..." -WorkingDirectory "d:\Projeler\Grifirio\src\services\Grafirio.Gateway" -Command "dotnet run"

Write-Host ""
Write-Host "[4/6] Starting frontend apps..." -ForegroundColor Yellow
Start-DetachedPowerShell -Title "Main Frontend starting..." -WorkingDirectory "d:\Projeler\Grifirio\src\front\grifirio.front" -Command "npm run dev"
Start-DetachedPowerShell -Title "Project Admin starting..." -WorkingDirectory "d:\Projeler\Grifirio\src\front\admins\grifirio.projectadmin" -Command "npm run dev"
Start-DetachedPowerShell -Title "User Admin starting..." -WorkingDirectory "d:\Projeler\Grifirio\src\front\admins\grifirio.useradmin" -Command "npm run dev"

Write-Host ""
Write-Host "[5/6] Starting AI service (Docker)..." -ForegroundColor Yellow
$dockerExe = "C:\Program Files\Docker\Docker\resources\bin\docker-compose.exe"
if (Test-Path $dockerExe) {
    Push-Location "d:\Projeler\Grifirio"
    # Hibrit mod: .NET host'ta kosuyor, motor konteynerde. Motorun SQL geri
    # cagrisi konteyner adina degil host'taki API'ye gitmeli.
    $env:PYCARET_DATA_ANALYSIS_API_URL = "http://host.docker.internal:5221"
    & $dockerExe up -d --build pycaret.engine 2>&1 | Out-Null
    Pop-Location
    Write-Host "  PyCaret Engine container started (port 8002)" -ForegroundColor Green
} else {
    Write-Host "  Docker not found — skipping AI service" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "[6/6] Done. Services were launched in separate terminals." -ForegroundColor Yellow
Write-Host "  Run/Debug compound: Grafirio - All Apps" -ForegroundColor Cyan
Write-Host ""
Write-Host "If Docker services are needed, start Docker Desktop and run the Docker compose stack separately." -ForegroundColor Gray
<#
# 1. Docker kontrolü ve başlatma
Write-Host "[1/5] Docker kontrolü..." -ForegroundColor Yellow
$dockerRunning = $false
try {
    docker info 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $dockerRunning = $true
        Write-Host "  ✓ Docker zaten çalışıyor" -ForegroundColor Green
    }
} catch {
    Write-Host "  ✗ Docker çalışmıyor" -ForegroundColor Red
}

if (-not $dockerRunning) {
    Write-Host "  → Docker Desktop başlatılıyor..." -ForegroundColor Yellow
    try {
        Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe" -ErrorAction Stop
        Write-Host "  → Docker'ın başlaması için 60 saniye bekleniyor..." -ForegroundColor Yellow
        Start-Sleep -Seconds 60
        
        # Tekrar kontrol
        docker info 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $dockerRunning = $true
            Write-Host "  ✓ Docker başarıyla başlatıldı" -ForegroundColor Green
        } else {
            Write-Host "  ⚠ Docker başlatılamadı, AI servisleri atlanacak" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  ⚠ Docker Desktop bulunamadı" -ForegroundColor Yellow
    }
}

Write-Host ""

# 2. Docker servisleri (AI Services)
if ($dockerRunning) {
    Write-Host "[2/5] Docker servisleri başlatılıyor..." -ForegroundColor Yellow
    try {
        Set-Location "d:\Projeler\Grifirio"
        docker-compose up -d mongo.db sqlserver.db.order postgres.db.keycloak postgres.db.dataanalysis rabbitmq redis.db.basket pycaret.engine 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✓ AI servisleri başlatıldı" -ForegroundColor Green
            Write-Host "    - MongoDB (27017)" -ForegroundColor Gray
            Write-Host "    - RabbitMQ (5672, 15672)" -ForegroundColor Gray
            Write-Host "    - Redis (6379)" -ForegroundColor Gray
            Write-Host "    - PyCaret Engine (8002)" -ForegroundColor Gray
        } else {
            Write-Host "  ⚠ Docker servisleri başlatılamadı" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  ⚠ Docker Compose hatası: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "[2/5] Docker servisleri atlandı (Docker çalışmıyor)" -ForegroundColor Yellow
}

Write-Host ""

# 3. .NET Microservices
Write-Host "[3/5] .NET Microservices başlatılıyor..." -ForegroundColor Yellow

# Identity API
try {
    Start-Process powershell -ArgumentList "-NoExit", "-Command", `
        "Write-Host 'Identity API başlatılıyor...' -ForegroundColor Cyan; " + `
        "cd 'd:\Projeler\Grifirio\src\services\Grafirio.Identity.Api\Grafirio.Identity.Api'; " + `
        "dotnet run" `
        -WindowStyle Normal -ErrorAction Stop
    Write-Host "  ✓ Identity API başlatıldı (5036)" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Identity API başlatılamadı" -ForegroundColor Red
}

Start-Sleep -Seconds 2

# Catalog API
try {
    Start-Process powershell -ArgumentList "-NoExit", "-Command", `
        "Write-Host 'Catalog API başlatılıyor...' -ForegroundColor Cyan; " + `
        "cd 'd:\Projeler\Grifirio\src\services\Grafirio.Catalog.Api'; " + `
        "dotnet run" `
        -WindowStyle Normal -ErrorAction Stop
    Write-Host "  ✓ Catalog API başlatıldı (5280)" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Catalog API başlatılamadı" -ForegroundColor Red
}

Start-Sleep -Seconds 2

# Basket API
try {
    Start-Process powershell -ArgumentList "-NoExit", "-Command", `
        "Write-Host 'Basket API başlatılıyor...' -ForegroundColor Cyan; " + `
        "cd 'd:\Projeler\Grifirio\src\services\Grafirio.Basket.Api'; " + `
        "dotnet run" `
        -WindowStyle Normal -ErrorAction Stop
    Write-Host "  ✓ Basket API başlatıldı (5023)" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Basket API başlatılamadı" -ForegroundColor Red
}

Write-Host ""

# 4. Frontend
Write-Host "[4/5] Frontend başlatılıyor..." -ForegroundColor Yellow
try {
    Start-Process powershell -ArgumentList "-NoExit", "-Command", `
        "Write-Host 'Frontend başlatılıyor...' -ForegroundColor Cyan; " + `
        "cd 'd:\Projeler\Grifirio\src\front\grifirio.front'; " + `
        "npm run dev" `
        -WindowStyle Normal -ErrorAction Stop
    Write-Host "  ✓ Frontend başlatıldı (59264)" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Frontend başlatılamadı" -ForegroundColor Red
}

Write-Host ""

# 5. Test sayfasını aç
Write-Host "[5/5] Servislerin başlaması bekleniyor..." -ForegroundColor Yellow
Start-Sleep -Seconds 15

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  ✓ TÜM SERVİSLER BAŞLATILDI!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

Write-Host "Erişim URL'leri:" -ForegroundColor Cyan
Write-Host "  → Test Sayfası:      http://localhost:59264/test-ai-services.html" -ForegroundColor White
Write-Host "  → AI Dashboard:      http://localhost:59264/ai-dashboard-public" -ForegroundColor White
Write-Host "  → Frontend:          http://localhost:59264" -ForegroundColor White
Write-Host "  → Identity API:      http://localhost:5036/swagger" -ForegroundColor White
Write-Host "  → Catalog API:       http://localhost:5280/swagger" -ForegroundColor White
Write-Host "  → Basket API:        http://localhost:5023/swagger" -ForegroundColor White

if ($dockerRunning) {
    Write-Host "  → Schema Analyzer:   http://localhost:8001" -ForegroundColor White
    Write-Host "  → PyCaret Engine:    http://localhost:8002" -ForegroundColor White
    Write-Host "  → RabbitMQ Mgmt:     http://localhost:15672" -ForegroundColor White
}

Write-Host ""
Write-Host "Test sayfası açılıyor..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

try {
    Start-Process "http://localhost:59264/test-ai-services.html"
} catch {
    Write-Host "Test sayfası otomatik açılamadı. Manuel olarak açın:" -ForegroundColor Yellow
    Write-Host "http://localhost:59264/test-ai-services.html" -ForegroundColor White
}

Write-Host ""
Write-Host "Bu pencereyi kapatabilirsiniz." -ForegroundColor Gray
Write-Host "Servisleri kapatmak için her terminal penceresini kapatın." -ForegroundColor Gray
Write-Host ""

# Script'in kapanmaması için bekle
Read-Host "Çıkmak için Enter'a basın"
#>
