# 🚀 Grafirio - Manuel Başlatma Scripti

## Docker Sorunu Çözümü

### Sorun: Docker Engine Internal Server Error
```
ERROR: request returned Internal Server Error for API route
```

### Çözüm Adımları:

#### 1. Docker Desktop'ı Tamamen Kapat ve Yeniden Başlat
```powershell
# Tüm Docker process'lerini kapat
Get-Process "*docker*" | Stop-Process -Force

# 5 saniye bekle
Start-Sleep -Seconds 5

# Docker Desktop'ı yeniden başlat
Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"

# 1 dakika bekle (Docker tamamen başlasın)
Start-Sleep -Seconds 60

# Test et
docker ps
```

#### 2. WSL2 Backend'i Yeniden Başlat
```powershell
# WSL'i tamamen kapat
wsl --shutdown

# Docker Desktop'ı yeniden başlat
Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"

# Test et
docker info
```

#### 3. Docker Desktop Settings Kontrolü
1. Docker Desktop'ı aç
2. Settings > General
3. "Use the WSL 2 based engine" işaretli olmalı
4. Settings > Resources > WSL Integration
5. Tüm distro'lar enabled olmalı
6. "Apply & Restart"

---

## Manuel Servis Başlatma

### ✅ Şu Anda Çalışan Servisler:

#### .NET Microservices (Ayrı Terminal'lerde)
- ✅ Identity API - http://localhost:5036
- ✅ Catalog API - http://localhost:5280
- ✅ Basket API - http://localhost:5023

#### Frontend
- ✅ React App - http://localhost:59264

---

## AI Servisleri İçin Alternatif Yöntemler

### Yöntem 1: Docker Düzelince (Önerilen)
```powershell
cd "d:\Projeler\Grifirio"

# Tüm AI servislerini başlat
docker-compose up -d rabbitmq redis.db.basket schema.analyzer pycaret.engine django.ai celery.worker

# Durumu kontrol et
docker ps
```

### Yöntem 2: Local Python Environment (Docker Olmadan)

#### A. RabbitMQ ve Redis (Chocolatey ile)
```powershell
# Chocolatey ile kurulum
choco install rabbitmq redis-64 -y

# RabbitMQ başlat
net start RabbitMQ

# Redis başlat
redis-server
```

#### B. Python AI Servisleri

##### Schema Analyzer
```powershell
cd "d:\Projeler\Grifirio\src\ai\ai-service-schema-analyzer"

# Virtual environment oluştur
python -m venv venv
.\venv\Scripts\Activate.ps1

# Paketleri yükle
pip install -r requirements.txt

# .env dosyasını hazırla
copy .env.example .env
# OPENAI_API_KEY'i düzenle

# Servisi başlat
uvicorn main:app --host 0.0.0.0 --port 8001 --reload
```

##### PyCaret Engine
```powershell
cd "d:\Projeler\Grifirio\src\ai\ai-service-pycaret-engine"

# Virtual environment oluştur
python -m venv venv
.\venv\Scripts\Activate.ps1

# Paketleri yükle
pip install -r requirements.txt

# Servisi başlat
uvicorn main:app --host 0.0.0.0 --port 8002 --reload
```

##### Django AI Service
```powershell
cd "d:\Projeler\Grifirio\src\ai\grafirio-ai-service"

# Virtual environment oluştur
python -m venv venv
.\venv\Scripts\Activate.ps1

# Paketleri yükle
pip install -r requirements.txt

# .env dosyasını hazırla
copy .env.example .env

# Migration
python manage.py migrate

# Celery Worker (yeni terminal)
celery -A config worker --loglevel=info

# Django Server
python manage.py runserver 8000
```

---

## Hızlı Başlatma Script'i

### AllServices.ps1
```powershell
# Tüm servisleri başlat

# 1. Docker kontrolü
Write-Host "Docker kontrolü..." -ForegroundColor Cyan
$dockerRunning = docker info 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker başlatılıyor..." -ForegroundColor Yellow
    Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"
    Start-Sleep -Seconds 60
}

# 2. Docker servisleri
Write-Host "Docker servisleri başlatılıyor..." -ForegroundColor Cyan
docker-compose up -d rabbitmq redis.db.basket schema.analyzer pycaret.engine django.ai celery.worker

# 3. .NET servisleri
Write-Host ".NET servisleri başlatılıyor..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd 'd:\Projeler\Grifirio\src\services\Grafirio.Identity.Api\Grafirio.Identity.Api'; dotnet run"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd 'd:\Projeler\Grifirio\src\services\Grafirio.Catalog.Api'; dotnet run"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd 'd:\Projeler\Grifirio\src\services\Grafirio.Basket.Api'; dotnet run"

# 4. Frontend
Write-Host "Frontend başlatılıyor..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd 'd:\Projeler\Grifirio\src\front\grifirio.front'; npm run dev"

# 5. Test sayfasını aç
Start-Sleep -Seconds 10
Start-Process "http://localhost:59264/test-ai-services.html"

Write-Host "Tüm servisler başlatıldı!" -ForegroundColor Green
```

---

## Servis Durumu Kontrolü

```powershell
# .NET Servisleri
Invoke-WebRequest http://localhost:5036/swagger -UseBasicParsing
Invoke-WebRequest http://localhost:5280/swagger -UseBasicParsing
Invoke-WebRequest http://localhost:5023/swagger -UseBasicParsing

# AI Servisleri
Invoke-WebRequest http://localhost:8001 -UseBasicParsing
Invoke-WebRequest http://localhost:8002 -UseBasicParsing
Invoke-WebRequest http://localhost:8000 -UseBasicParsing

# Frontend
Invoke-WebRequest http://localhost:59264 -UseBasicParsing
```

---

## Sorun Giderme

### Docker "Internal Server Error"
**Neden:** WSL2 backend veya Docker Engine çökmüş olabilir

**Çözüm:**
1. WSL'i tamamen kapat: `wsl --shutdown`
2. Docker Desktop'ı kapat: `Get-Process "*docker*" | Stop-Process -Force`
3. Docker Desktop'ı yeniden başlat
4. 1-2 dakika bekle

### Port Çakışması
```powershell
# Hangi process kullanıyor?
netstat -ano | findstr :8001
netstat -ano | findstr :8002

# Process'i kapat
Stop-Process -Id <PID> -Force
```

### Python Virtual Environment Hataları
```powershell
# Yeniden oluştur
Remove-Item -Recurse -Force venv
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install --upgrade pip
pip install -r requirements.txt
```

---

## Şu Anki Durum

### ✅ Çalışan:
- Identity API (5036)
- Catalog API (5280)
- Basket API (5023)
- Frontend (59264)

### ⏳ Bekleyen (Docker):
- RabbitMQ
- Redis
- Schema Analyzer
- PyCaret Engine
- Django AI
- Celery Worker

### 🎯 Sonraki Adım:
1. Docker'ı düzelt (WSL2 yeniden başlat)
2. AI servislerini başlat
3. Test sayfasını aç: http://localhost:59264/test-ai-services.html

---

## Hızlı Komutlar

```powershell
# Docker'ı yeniden başlat
wsl --shutdown; Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"

# AI servislerini başlat (Docker düzelince)
cd "d:\Projeler\Grifirio"; docker-compose up -d schema.analyzer pycaret.engine django.ai celery.worker rabbitmq redis.db.basket

# Tüm servisleri kontrol et
docker ps; netstat -ano | findstr :5036; netstat -ano | findstr :59264

# Test sayfasını aç
Start-Process "http://localhost:59264/test-ai-services.html"
```
