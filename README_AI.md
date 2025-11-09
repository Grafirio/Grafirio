# 🎯 Grafirio AI - Hızlı Erişim Rehberi

## 🚀 ÖNEMLİ LINKLER

### ✅ ÇALIŞAN SERVİSLER

#### Frontend
- **Test Sayfası**: http://localhost:59264/test-ai-services.html
- **AI Dashboard (Public)**: http://localhost:59264/ai-dashboard-public
- **AI Dashboard (Auth)**: http://localhost:59264/ai-dashboard
- **Ana Sayfa**: http://localhost:59264

#### AI Services
- **Schema Analyzer**: http://localhost:8001 ([Docs](http://localhost:8001/docs))
- **PyCaret Engine**: http://localhost:8002 ([Docs](http://localhost:8002/docs))
- **Django AI**: http://localhost:8000
- **WebSocket**: `ws://localhost:8000/ws/company/{id}/`

#### .NET APIs
- **Identity API**: http://localhost:5036/swagger
- **Catalog API**: http://localhost:5280/swagger
- **Basket API**: http://localhost:5023/swagger

#### Infrastructure
- **RabbitMQ**: http://localhost:15672 (guest/guest123)
- **Keycloak**: http://localhost:8080 (admin/password)

---

## ⚡ HIZLI BAŞLATMA

### 1. Test Sayfasını Aç
```
http://localhost:59264/test-ai-services.html
```
Tüm servislerin "Online" olduğunu doğrula.

### 2. AI Dashboard'ı Aç
```
http://localhost:59264/ai-dashboard-public
```

### 3. İlk Test
1. **Database Connection** tab
2. Bilgileri gir (PostgreSQL/MySQL/MSSQL)
3. **"Test Connection"** → Başarılı olmalı
4. **"Analyze Schema"** → Schema analizi yapılır
5. **"Schema Analysis"** tab → Sonuçları gör

---

## 🛠️ SORUN GİDERME

### "Not Found" Hatası
Public route kullan: http://localhost:59264/ai-dashboard-public

### AI Servisleri Offline
```powershell
docker restart django.ai.container pycaret.engine.container schema.analyzer.container
```

### CORS Hatası
Django settings'e ekle:
```python
CORS_ALLOWED_ORIGINS = ["http://localhost:59264"]
```

### Log Kontrolü
```powershell
docker logs django.ai.container --tail 50
docker logs pycaret.engine.container --tail 50
```

---

## 📊 SİSTEM MİMARİSİ

```
Frontend (React) → AI Dashboard
    ↓
AI Services:
    • Schema Analyzer (Pandas + OpenAI GPT-4o)
    • PyCaret Engine (AutoML)
    • Django AI (CDC + WebSocket)
    ↓
Message Broker: RabbitMQ
    ↓
.NET Microservices (Identity, Catalog, Basket)
    ↓
Databases (MongoDB, SQL Server, PostgreSQL, Redis)
```

---

## 📚 DOKÜMANTASYON

- [**Quick Start**](./QUICK_START.md) - Detaylı başlangıç rehberi
- [**Frontend Integration**](./AI_FRONTEND_INTEGRATION_COMPLETE.md) - Frontend entegrasyonu
- [**AI Services**](./AI_COMPLETE_SUMMARY.md) - AI servisleri dokümantasyonu
- [**MassTransit Setup**](./AI_INTEGRATION_SUMMARY.md) - Message broker kurulumu

---

## ✅ KONTROL LİSTESİ

- [ ] Test sayfası açılıyor
- [ ] Tüm servisler "Online"
- [ ] AI Dashboard açılıyor
- [ ] Database connection çalışıyor
- [ ] Schema analizi yapılabiliyor
- [ ] WebSocket bağlanıyor

---

## 🎯 SONUÇ

**Sistem hazır ve çalışıyor!**

Test için: http://localhost:59264/test-ai-services.html

AI Dashboard: http://localhost:59264/ai-dashboard-public

Sorun mu var? [QUICK_START.md](./QUICK_START.md) dosyasına bakın.
