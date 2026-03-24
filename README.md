# Grafirio - Sistem Rehberi

## Servisler ve Portlar

### Backend APIs
| Servis | Port | Swagger | Health |
|--------|------|---------|--------|
| Gateway (YARP) | 5000 | - | http://localhost:5000/health |
| Identity API | 5036 | http://localhost:5036/swagger | http://localhost:5036/health |
| Catalog API | 5280 | http://localhost:5280/swagger | http://localhost:5280/health |
| Basket API | 5023 | http://localhost:5023/swagger | http://localhost:5023/health |
| Data Analysis API | 5221 | http://localhost:5221/swagger | http://localhost:5221/health |

### Frontend
| Uygulama | URL |
|----------|-----|
| Ana Sayfa | http://localhost:59264 |
| AI Dashboard (public) | http://localhost:59264/ai-dashboard-public |
| AI Dashboard (auth) | http://localhost:59264/ai-dashboard |
| Servis Test Sayfası | http://localhost:59264/test-ai-services.html |
| Data Analysis | http://localhost:59264/data-analysis-public |

### AI Services
| Servis | URL | Docs |
|--------|-----|------|
| Schema Analyzer | http://localhost:8001 | http://localhost:8001/docs |
| PyCaret AutoML | http://localhost:8002 | http://localhost:8002/docs |
| Django AI Service | http://localhost:8000 | - |
| WebSocket | ws://localhost:8000/ws/company/{id}/ | - |

### Infrastructure
| Servis | URL | Kimlik |
|--------|-----|--------|
| RabbitMQ | http://localhost:15672 | guest / guest123 |
| Keycloak | http://localhost:8080 | admin / password |
| PgAdmin | http://localhost:8888 | - |
| MongoDB Express | http://localhost:27032 | - |
| Redis Commander | http://localhost:27033 | - |

---

## Hızlı Başlangıç

### 1. Sistemi Başlat

```powershell
# Docker ile (önerilen)
.\StartWithDocker.ps1

# Veya doğrudan
docker-compose up -d --build
```

### 2. Servisleri Doğrula

```powershell
docker ps --format "table {{.Names}}`t{{.Status}}`t{{.Ports}}"
```

Test sayfasından tüm servislerin "Online" olduğunu kontrol et:
```
http://localhost:59264/test-ai-services.html
```

### 3. AI Dashboard

**Development (auth yok):**
```
http://localhost:59264/ai-dashboard-public
```

**Production (Keycloak ile):**
```
http://localhost:59264/ai-dashboard
```

### 4. İlk Schema Analizi

1. **Database Connection** tab'ına git
2. Bağlantı bilgilerini gir (PostgreSQL/MySQL/MSSQL)
3. **"Test Connection"** butonuna tıkla
4. **"Analyze Schema"** butonuna tıkla
5. **Schema Analysis** tab'ında sonuçları gör

---

## Mimari

```
Frontend (React)
    ↓
Gateway (YARP :5000)
    ↓
.NET Microservices (Identity, Catalog, Basket, DataAnalysis)
    ↓
RabbitMQ (Message Broker)
    ↓
AI Services (Django AI → NLP → SQL → Database)
    • Schema Analyzer (Pandas + OpenAI GPT-4o)
    • PyCaret Engine (AutoML)
    • Celery Worker (Background tasks)
    ↓
Databases (MongoDB, SQL Server, PostgreSQL, Redis)
```

---

## Sorun Giderme

### "Not Found" Hatası
Public route kullan:
```
http://localhost:59264/ai-dashboard-public
```

### AI Servisleri Offline
```powershell
docker restart django.ai.container pycaret.engine.container schema.analyzer.container
docker logs django.ai.container --tail 50
```

### CORS Hatası
```python
# src/ai/grafirio-ai-service/config/settings.py
CORS_ALLOWED_ORIGINS = [
    "http://localhost:59264",
    "http://localhost:3000",
]
CORS_ALLOW_CREDENTIALS = True
```

### WebSocket Bağlanamıyor
```powershell
# URL kontrolü
docker logs django.ai.container | Select-String -Pattern "websocket|daphne"
```
Frontend `.env`:
```
VITE_DJANGO_AI_WS_URL=ws://localhost:8000
```

### Port Çakışması
```powershell
netstat -ano | findstr :PORT_NUMARASI
Stop-Process -Id <PID> -Force
```

---

## İlgili Dökümanlar

- [DOCKER.md](DOCKER.md) - Docker komutları ve sorun giderme
- [TESTING_GUIDE.md](TESTING_GUIDE.md) - Test senaryoları
- [DATA_ANALYSIS_README.md](DATA_ANALYSIS_README.md) - Data Analysis API detayları
- [Keycloak_Request_Postman.md](Keycloak_Request_Postman.md) - Keycloak curl örnekleri
