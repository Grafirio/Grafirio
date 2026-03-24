# Grafirio - Docker Kullanım Kılavuzu

## 🚀 Tek Komutla Sistemi Başlatma

Artık tüm Grafirio sistemini (Infrastructure + .NET APIs + AI Services + Frontend) **tek bir komutla** başlatabilirsiniz:

```powershell
# Yol 1: PowerShell script ile (Önerilen)
.\StartWithDocker.ps1

# Yol 2: Doğrudan docker-compose ile
docker-compose up -d --build
```

## 📦 Dockerized Servisler

### Infrastructure (6 servis)
| Servis | Container | Port | UI Port |
|--------|-----------|------|---------|
| MongoDB Catalog | `mongo.db.catalog.container` | 27030 | 27032 |
| MongoDB Discount | `mongo.db.discount.container` | 27034 | - |
| Redis Basket | `redis.db.container` | 6379 | 27033 |
| PostgreSQL Keycloak | `postgres.db.keycloak.container` | 5432 | 8888 |
| SQL Server Order | `sqlserver.db.order.container` | 1433 | - |
| RabbitMQ | `rabbitmq.container` | 5672 | 15672 |

### .NET Microservices (5 servis)
| Servis | Container | Port | Health |
|--------|-----------|------|--------|
| Identity API | `identity.api.container` | 5036 | ✅ |
| Catalog API | `catalog.api.container` | 5280 | ✅ |
| Basket API | `basket.api.container` | 5023 | ✅ |
| Data Analysis API | `data-analysis.api.container` | 5221 | ✅ |
| Gateway (YARP) | `gateway.container` | 5000 | ✅ |

### AI Services (4 servis)
| Servis | Container | Port | Framework |
|--------|-----------|------|-----------|
| Django AI Service | `django.ai.container` | 8000 | Django + Celery |
| Celery Worker | `celery.worker.container` | - | Celery |
| Schema Analyzer | `schema.analyzer.container` | 8001 | FastAPI |
| PyCaret AutoML | `pycaret.engine.container` | 8002 | FastAPI + PyCaret |

## 🔧 Docker Komutları

### Sistemi Başlatma
```powershell
# Tüm servisleri başlat (arka planda)
docker-compose up -d

# Tüm servisleri başlat + rebuild (değişiklikler varsa)
docker-compose up -d --build

# Belirli servisleri başlat
docker-compose up -d identity.api catalog.api gateway
```

### Sistemi Durdurma
```powershell
# Tüm servisleri durdur
docker-compose down

# Servisleri durdur + volumeleri sil (veritabanı verilerini temizle)
docker-compose down -v

# Servisleri durdur + image'ları sil
docker-compose down --rmi all
```

### Durum Kontrolü
```powershell
# Çalışan container'ları listele
docker-compose ps

# Detaylı container bilgisi
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

# Container logları
docker-compose logs -f [service-name]

# Örnek: Identity API logları
docker-compose logs -f identity.api
```

### Yeniden Başlatma
```powershell
# Tüm servisleri yeniden başlat
docker-compose restart

# Belirli bir servisi yeniden başlat
docker-compose restart identity.api

# Servisi durdur ve yeniden başlat
docker-compose stop identity.api
docker-compose start identity.api
```

### Debugging
```powershell
# Container içine gir (bash)
docker exec -it identity.api.container /bin/bash

# Container loglarını izle (realtime)
docker-compose logs -f --tail=100 identity.api

# Tüm servislerin loglarını izle
docker-compose logs -f

# Container resource kullanımı
docker stats
```

## 🏗️ Build & Geliştirme

### Kod Değişikliklerinden Sonra
```powershell
# .NET API'lerde değişiklik yaptıysanız
docker-compose up -d --build identity.api

# Python AI servislerinde değişiklik yaptıysanız
docker-compose up -d --build django.ai

# Tüm .NET servisleri rebuild
docker-compose up -d --build identity.api catalog.api basket.api data-analysis.api gateway
```

### Cache Sorunları
```powershell
# Docker build cache'i temizle
docker builder prune -a

# Tüm servisleri sıfırdan build et
docker-compose build --no-cache

# Belirli bir servisi sıfırdan build et
docker-compose build --no-cache identity.api
```

## 🔍 Health Check URLs

```
# Gateway (All services accessible through here)
http://localhost:5000/health

# Individual APIs
http://localhost:5036/health  # Identity
http://localhost:5280/health  # Catalog
http://localhost:5023/health  # Basket
http://localhost:5221/health  # Data Analysis

# AI Services
http://localhost:8000/health  # Django AI
http://localhost:8001/health  # Schema Analyzer
http://localhost:8002/health  # PyCaret

# Infrastructure UIs
http://localhost:15672        # RabbitMQ Management (guest/guest)
http://localhost:27032        # MongoDB Express
http://localhost:27033        # Redis Commander
http://localhost:8888         # PgAdmin
```

## 🌐 Network Yapısı

Tüm servisler `grifirio_network` bridge network'ünde çalışır ve birbirleriyle container adlarıyla haberleşebilirler:

```yaml
# Örnek: Catalog API, MongoDB'ye şu şekilde bağlanır:
mongodb://mongo.db.catalog:27017

# Örnek: Basket API, Redis'e şu şekilde bağlanır:
redis.db.basket:6379

# Örnek: Gateway, Identity API'yi şu şekilde çağırır:
http://identity.api:5036
```

## 📊 Volume Yönetimi

### Veritabanı Verileri
```powershell
# Tüm volumeleri listele
docker volume ls

# Belirli bir volume'ü incele
docker volume inspect grifirio_mongo.db.catalog.volume

# Tüm volumeleri sil (VERİLER SİLİNİR!)
docker-compose down -v
```

### Volume Yedekleme
```powershell
# PostgreSQL yedek
docker exec postgres.db.keycloak.container pg_dump -U keycloak keycloak > backup.sql

# MongoDB yedek
docker exec mongo.db.catalog.container mongodump --username root --password password --out /backup
```

## 🚨 Sorun Giderme

### Container Başlamıyor
```powershell
# 1. Logları kontrol et
docker-compose logs [service-name]

# 2. Port çakışması var mı?
netstat -ano | findstr :5036

# 3. Container'ı yeniden oluştur
docker-compose up -d --force-recreate [service-name]
```

### Build Hatası
```powershell
# 1. Cache'i temizle
docker builder prune -a

# 2. Sıfırdan build et
docker-compose build --no-cache [service-name]

# 3. Docker Daemon'ı yeniden başlat
Restart-Service docker  # (Elevated PowerShell)
```

### Performans Sorunları
```powershell
# 1. Resource kullanımını kontrol et
docker stats

# 2. Gereksiz image'ları temizle
docker image prune -a

# 3. Docker Desktop'ta memory/CPU limitlerini artır
# Settings > Resources > Advanced
```

## 🎯 Hızlı Komut Referansı

```powershell
# Başlat
docker-compose up -d

# Durdur
docker-compose down

# Yeniden Başlat
docker-compose restart

# Logları İzle
docker-compose logs -f

# Durum Kontrol
docker-compose ps

# rebuild + başlat
docker-compose up -d --build

# Bellek Temizliği
docker system prune -a --volumes
```

## 💡 Pro Tips

1. **İlk başlatma uzun sürer** (5-10 dk): .NET SDK ve Python image'ları build edilir
2. **İkinci başlatma hızlıdır** (30-60 sn): Cache'den image'lar kullanılır
3. **development profili için** `.env` dosyasını kontrol edin
4. **Production için** environment variables'ı güvenli şekilde yönetin
5. **Keycloak** production'da mutlaka `localhost` dışında çalıştırın

## 🔗 İlgili Dökümanlar

- [QUICK_START.md](QUICK_START.md) - Genel sistem başlangıç rehberi
- [MANUAL_START_GUIDE.md](MANUAL_START_GUIDE.md) - Manuel başlatma (Docker olmadan)
- [IMPLEMENTATION_SUMMARY.md](IMPLEMENTATION_SUMMARY.md) - Son implementasyonlar
- [TESTING_GUIDE.md](TESTING_GUIDE.md) - Test senaryoları

---

**Not**: Frontend (React) henüz dockerize edilmedi. Frontend'i manuel olarak başlatmak için:
```powershell
cd src/front/grifirio.front
npm run dev
```
