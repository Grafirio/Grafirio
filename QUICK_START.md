# 🚀 Grafirio AI - Quick Start Guide

## ⚡ Hızlı Başlangıç (5 Dakika)

### 1️⃣ Servisleri Kontrol Et

Test sayfasını aç:
```
http://localhost:59264/test-ai-services.html
```

**Tüm servisler "Online" olmalı:**
- ✅ Schema Analyzer
- ✅ PyCaret Engine
- ✅ Django AI Service
- ✅ RabbitMQ

### 2️⃣ AI Dashboard'a Eriş

**Keycloak Olmadan (Development):**
```
http://localhost:59264/ai-dashboard-public
```

**Keycloak İle (Production):**
```
http://localhost:59264/ai-dashboard
```

### 3️⃣ İlk Schema Analizi

1. **Database Connection** tab'ına git
2. Bilgileri gir:
   ```
   Database Type: PostgreSQL
   Host: localhost
   Port: 5432
   Database: postgres
   Username: postgres
   Password: postgres
   ```
3. **"Test Connection"** butonuna tıkla
4. **"Analyze Schema"** butonuna tıkla
5. **Schema Analysis** tab'ında sonuçları gör

### 4️⃣ Model Eğit

1. **Model Training** tab'ına git
2. **"Train New Model"** butonuna tıkla
3. Training status'u izle
4. Model tamamlandığında metrics'leri gör

### 5️⃣ Real-time Predictions

1. **Real-time Predictions** tab'ına git
2. WebSocket bağlantısı otomatik başlar
3. Celery CDC polling çalıştığında tahminler gelir

---

## 🔧 Servis Durumlarını Kontrol

### Docker Container'ları
```powershell
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

**Çalışması Gerekenler:**
- pycaret.engine.container
- django.ai.container
- celery.worker.container
- schema.analyzer.container
- rabbitmq.container
- redis.db.container

### .NET Servisleri
```powershell
# Identity API
curl http://localhost:5036/swagger

# Catalog API
curl http://localhost:5280/swagger

# Basket API
curl http://localhost:5023/swagger
```

### AI Servisleri
```powershell
# Schema Analyzer
curl http://localhost:8001

# PyCaret Engine
curl http://localhost:8002

# Django AI (Not: CORS olabilir)
curl http://localhost:8000
```

---

## 🐛 Sorun Giderme

### Sorun: "Not Found" Hatası

**Çözüm 1: Public route kullan**
```
http://localhost:59264/ai-dashboard-public
```

**Çözüm 2: Keycloak'ı devre dışı bırak**
```jsx
// src/main.jsx
// initOptions'ı değiştir:
initOptions={{
  onLoad: 'check-sso',  // login-required yerine
  checkLoginIframe: false,
}}
```

### Sorun: AI Servisleri Offline

**Docker container'ları kontrol et:**
```powershell
docker ps -a | Select-String -Pattern "ai|rabbitmq|redis"
```

**Container'ları restart et:**
```powershell
docker restart django.ai.container celery.worker.container pycaret.engine.container schema.analyzer.container
```

**Log'ları kontrol et:**
```powershell
docker logs django.ai.container --tail 50
docker logs pycaret.engine.container --tail 50
docker logs schema.analyzer.container --tail 50
```

### Sorun: CORS Hatası

**Django'ya CORS ekle:**
```python
# src/ai/grafirio-ai-service/config/settings.py

CORS_ALLOWED_ORIGINS = [
    "http://localhost:59264",
    "http://localhost:3000",
]

CORS_ALLOW_CREDENTIALS = True
```

**Container'ı rebuild et:**
```powershell
docker-compose build django.ai
docker-compose up -d django.ai
```

### Sorun: WebSocket Bağlanamıyor

**Django Channels kontrolü:**
```powershell
docker logs django.ai.container | Select-String -Pattern "websocket|daphne"
```

**URL'i kontrol et:**
```javascript
// Frontend .env
VITE_DJANGO_AI_WS_URL=ws://localhost:8000
```

---

## 📚 Önemli URL'ler

### Frontend
| URL | Açıklama |
|-----|----------|
| http://localhost:59264 | Ana sayfa |
| http://localhost:59264/ai-dashboard-public | AI Dashboard (auth yok) |
| http://localhost:59264/test-ai-services.html | Test sayfası |

### AI Services
| Service | URL | Swagger/Docs |
|---------|-----|--------------|
| Schema Analyzer | http://localhost:8001 | http://localhost:8001/docs |
| PyCaret Engine | http://localhost:8002 | http://localhost:8002/docs |
| Django AI | http://localhost:8000 | - |

### Infrastructure
| Service | URL | Credentials |
|---------|-----|-------------|
| RabbitMQ | http://localhost:15672 | guest / guest123 |
| Keycloak | http://localhost:8080 | admin / password |

---

## 🎯 Test Senaryosu

### Scenario 1: Schema Analizi
```bash
# 1. Test sayfasını aç
http://localhost:59264/test-ai-services.html

# 2. Tüm servislerin "Online" olduğunu doğrula

# 3. AI Dashboard'ı aç
http://localhost:59264/ai-dashboard-public

# 4. Database Connection tab'ına git

# 5. PostgreSQL bilgilerini gir ve "Test Connection"

# 6. "Analyze Schema" butonuna tıkla

# 7. Schema Analysis tab'ında sonuçları kontrol et
```

### Scenario 2: Manuel Prediction
```bash
# PowerShell'de:
$body = @{
    company_id = "1"
    model_name = "test_model"
    data = @{
        feature1 = 10.5
        feature2 = 20.3
    }
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:8002/predict" -Method POST -Body $body -ContentType "application/json"
```

### Scenario 3: Real-time WebSocket
```javascript
// Browser Console'da:
const ws = new WebSocket('ws://localhost:8000/ws/company/1/');

ws.onopen = () => console.log('Connected!');
ws.onmessage = (e) => console.log('Message:', JSON.parse(e.data));
ws.onerror = (e) => console.error('Error:', e);
```

---

## 🔄 Yeniden Başlatma

### Tüm AI Servislerini Yeniden Başlat
```powershell
# Stop
docker-compose stop schema.analyzer pycaret.engine django.ai celery.worker rabbitmq redis.db.basket

# Start
docker-compose up -d schema.analyzer pycaret.engine django.ai celery.worker rabbitmq redis.db.basket

# Logs
docker-compose logs -f django.ai
```

### Frontend'i Yeniden Başlat
```powershell
cd "d:\Projeler\Grifirio\src\front\grifirio.front"
npm run dev
```

### .NET Servislerini Yeniden Başlat
```powershell
# Identity API
cd "d:\Projeler\Grifirio\src\services\Grafirio.Identity.Api\Grafirio.Identity.Api"
dotnet run
```

---

## ✅ Sistem Sağlık Kontrol Checklist

- [ ] Docker Desktop çalışıyor
- [ ] RabbitMQ container ayakta (port 5672, 15672)
- [ ] Redis container ayakta (port 6379)
- [ ] Schema Analyzer ayakta (port 8001)
- [ ] PyCaret Engine ayakta (port 8002)
- [ ] Django AI ayakta (port 8000)
- [ ] Celery Worker çalışıyor
- [ ] Frontend dev server çalışıyor (port 59264)
- [ ] Test sayfası erişilebilir
- [ ] AI Dashboard açılıyor

---

## 🚀 Production Deployment

### Docker Compose ile Tümünü Başlat
```powershell
cd "d:\Projeler\Grifirio"
docker-compose up -d
```

### Environment Variables
Production'da `.env` dosyasını güncelleyin:
```env
# Frontend .env
VITE_SCHEMA_ANALYZER_URL=https://schema-analyzer.yourdomain.com
VITE_PYCARET_ENGINE_URL=https://pycaret-engine.yourdomain.com
VITE_DJANGO_AI_URL=https://ai.yourdomain.com
VITE_DJANGO_AI_WS_URL=wss://ai.yourdomain.com
```

---

## 📞 Yardım

**Sorun mu yaşıyorsunuz?**

1. Test sayfasını kontrol edin: http://localhost:59264/test-ai-services.html
2. Docker logs bakın: `docker logs django.ai.container`
3. Frontend console'u kontrol edin (F12)
4. CORS hatası için Django settings'i güncelleyin

**Dokümantasyon:**
- [AI Services Documentation](./AI_COMPLETE_SUMMARY.md)
- [Frontend Integration](./AI_FRONTEND_INTEGRATION_COMPLETE.md)
- [MassTransit Setup](./AI_INTEGRATION_SUMMARY.md)

---

🎉 **Başarılar! Grafirio AI sistemi hazır!**
