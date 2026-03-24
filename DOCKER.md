# Grafirio - Docker Kılavuzu

## Sistemi Başlatma

```powershell
# PowerShell script ile (önerilen)
.\StartWithDocker.ps1

# Doğrudan docker-compose ile
docker-compose up -d --build
```

---

## Servis Tabloları

### Infrastructure
| Servis | Container | Port | UI Port |
|--------|-----------|------|---------|
| MongoDB Catalog | `mongo.db.catalog.container` | 27030 | 27032 |
| MongoDB Discount | `mongo.db.discount.container` | 27034 | - |
| Redis | `redis.db.container` | 6379 | 27033 |
| PostgreSQL (Keycloak) | `postgres.db.keycloak.container` | 5432 | 8888 |
| SQL Server | `sqlserver.db.order.container` | 1433 | - |
| RabbitMQ | `rabbitmq.container` | 5672 | 15672 |

### .NET Microservices
| Servis | Container | Port |
|--------|-----------|------|
| Gateway (YARP) | `gateway.container` | 5000 |
| Identity API | `identity.api.container` | 5036 |
| Catalog API | `catalog.api.container` | 5280 |
| Basket API | `basket.api.container` | 5023 |
| Data Analysis API | `data-analysis.api.container` | 5221 |

### AI Services
| Servis | Container | Port | Framework |
|--------|-----------|------|-----------|
| Django AI Service | `django.ai.container` | 8000 | Django + Celery |
| Celery Worker | `celery.worker.container` | - | Celery |
| Schema Analyzer | `schema.analyzer.container` | 8001 | FastAPI |
| PyCaret AutoML | `pycaret.engine.container` | 8002 | FastAPI + PyCaret |

> **Not:** Frontend (React) henüz dockerize edilmedi. Manuel başlatmak için: `cd src/front/grifirio.front && npm run dev`

---

## Docker Komut Referansı

### Başlatma / Durdurma
```powershell
docker-compose up -d                        # Başlat
docker-compose up -d --build                # Rebuild + başlat
docker-compose down                         # Durdur
docker-compose down -v                      # Durdur + volumeleri sil (DB verisi silinir!)
docker-compose restart                      # Yeniden başlat
docker-compose restart identity.api         # Belirli servisi yeniden başlat
```

### Durum / Log
```powershell
docker-compose ps
docker ps --format "table {{.Names}}`t{{.Status}}`t{{.Ports}}"
docker-compose logs -f                      # Tüm logları izle
docker-compose logs -f identity.api         # Belirli servis logu
docker stats                                # Resource kullanımı
```

### Debugging
```powershell
docker exec -it identity.api.container /bin/bash   # Container'a gir
docker-compose up -d --force-recreate identity.api # Container'ı yeniden oluştur
```

### Kod Değişikliklerinden Sonra
```powershell
# Tek servis rebuild
docker-compose up -d --build identity.api

# Tüm .NET servisleri rebuild
docker-compose up -d --build identity.api catalog.api basket.api data-analysis.api gateway
```

### Cache Temizliği
```powershell
docker builder prune -a                     # Build cache temizle
docker-compose build --no-cache             # Sıfırdan build
docker image prune -a                       # Kullanılmayan image'ları sil
docker system prune -a --volumes            # Tam temizlik (dikkatli!)
```

### Volume Yönetimi
```powershell
docker volume ls
docker volume inspect grifirio_mongo.db.catalog.volume

# PostgreSQL yedek
docker exec postgres.db.keycloak.container pg_dump -U keycloak keycloak > backup.sql
```

---

## Manuel Başlatma (Docker Olmadan)

### .NET Servisleri (Ayrı terminallerden)
```powershell
cd src/services/Grafirio.Identity.Api/Grafirio.Identity.Api; dotnet run
cd src/services/Grafirio.Catalog.Api; dotnet run
cd src/services/Grafirio.Basket.Api; dotnet run
cd src/services/Grafirio.DataAnalysis.Api; dotnet run
```

### Python AI Servisleri

#### Schema Analyzer
```powershell
cd "d:\Projeler\Grifirio\src\ai\ai-service-schema-analyzer"
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt
# .env dosyasını düzenle (OPENAI_API_KEY)
uvicorn main:app --host 0.0.0.0 --port 8001 --reload
```

#### PyCaret Engine
```powershell
cd "d:\Projeler\Grifirio\src\ai\ai-service-pycaret-engine"
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt
uvicorn main:app --host 0.0.0.0 --port 8002 --reload
```

#### Django AI Service
```powershell
cd "d:\Projeler\Grifirio\src\ai\grafirio-ai-service"
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt
python manage.py migrate
# Yeni terminalde Celery:
celery -A config worker --loglevel=info
# Django server:
python manage.py runserver 8000
```

#### RabbitMQ & Redis (Chocolatey ile)
```powershell
choco install rabbitmq redis-64 -y
net start RabbitMQ
redis-server
```

---

## Sorun Giderme

### Docker Engine Internal Server Error
```powershell
# WSL2'yi yeniden başlat
wsl --shutdown
Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"
Start-Sleep -Seconds 60
docker ps
```

### Container Başlamıyor
```powershell
docker-compose logs [service-name]           # Logları kontrol et
netstat -ano | findstr :5036                 # Port çakışması var mı?
docker-compose up -d --force-recreate [service-name]
```

### Build Hatası
```powershell
docker builder prune -a
docker-compose build --no-cache [service-name]
```

### Python Virtual Environment Hatası
```powershell
Remove-Item -Recurse -Force venv
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install --upgrade pip
pip install -r requirements.txt
```

### Docker Desktop Settings Kontrolü
1. Settings > General → "Use the WSL 2 based engine" işaretli olmalı
2. Settings > Resources > WSL Integration → Tüm distro'lar enabled olmalı
3. "Apply & Restart"

---

## Pro Tips

- **İlk başlatma** 5-10 dk sürer (.NET SDK ve Python image'ları build edilir)
- **Sonraki başlatmalar** 30-60 sn sürer (cache'den kullanılır)
- Keycloak'ı production'da `localhost` dışında çalıştırın
- `.env` dosyasını production için güvenli şekilde yönetin
