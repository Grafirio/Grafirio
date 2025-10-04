# 🎉 AI Services Kurulumu Tamamlandı!

## 📦 Oluşturulan Servisler

### 1. **Schema Analyzer Service** (Port 8001)
- **Görev:** DB'lere bağlanıp şema analizi yapar, LLM ile semantic anlam çıkarır
- **Teknoloji:** FastAPI + Pandas + OpenAI GPT-4o-mini
- **Endpoint:**
  - `POST /analyze` - Schema analizi
  - `POST /test-connection` - DB bağlantı testi

### 2. **PyCaret Engine Service** (Port 8002)
- **Görev:** Auto-ML model eğitimi ve real-time tahmin
- **Teknoloji:** FastAPI + PyCaret + Scikit-learn
- **Endpoint:**
  - `POST /train` - Model eğitimi (background task)
  - `GET /train/status/{company_id}` - Eğitim durumu
  - `POST /predict` - Real-time tahmin
  - `GET /models/{company_id}` - Eğitilmiş modelleri listele

### 3. **Django CDC + WebSocket** (Port 8000)
- **Görev:** DB polling, RabbitMQ integration, WebSocket push
- **Teknoloji:** Django + Celery + Channels
- **Endpoint:**
  - `ws://localhost:8000/ws/company/{company_id}/` - Real-time WebSocket

---

## 🚀 HIZLI BAŞLANGIÇ

### 1. OpenAI API Key Ayarla

```bash
# .env dosyasını düzenle
OPENAI_API_KEY=sk-your-actual-openai-api-key
```

### 2. Docker Servisleri Başlat

```bash
# RabbitMQ ve Redis zaten çalışıyor
docker-compose up -d rabbitmq redis.db.basket

# AI servisleri başlat
docker-compose up -d schema.analyzer pycaret.engine django.ai celery.worker
```

### 3. Logları İzle

```bash
# Schema Analyzer
docker logs -f schema.analyzer.container

# PyCaret Engine
docker logs -f pycaret.engine.container

# Django
docker logs -f django.ai.container

# Celery
docker logs -f celery.worker.container
```

---

## 🧪 TEST SENARYOSU

### Adım 1: Test DB Bağlantısı

```bash
curl -X POST http://localhost:8001/test-connection \
  -H "Content-Type: application/json" \
  -d '{
    "type": "postgresql",
    "host": "your-db-host",
    "port": 5432,
    "database": "test_db",
    "username": "user",
    "password": "pass"
  }'
```

**Beklenen Yanıt:**
```json
{
  "status": "success",
  "message": "Database connection successful"
}
```

---

### Adım 2: Schema Analizi

```bash
curl -X POST http://localhost:8001/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "company_id": "test-company-123",
    "company_name": "Test Kafesi",
    "db_connection": {
      "type": "postgresql",
      "host": "your-db-host",
      "port": 5432,
      "database": "cafe_db",
      "username": "readonly_user",
      "password": "password"
    }
  }'
```

**Beklenen Yanıt:**
```json
{
  "company_id": "test-company-123",
  "status": "success",
  "semantic_schema": {
    "industry": "restaurant",
    "industry_tr": "Restoran/Kafe",
    "confidence": 0.95,
    "tables": {
      "orders": {
        "semantic_name": "Siparişler",
        "description": "Müşteri siparişleri",
        "target_columns": ["total_amount"],
        "metric_columns": ["total_amount", "tip"],
        "dimension_columns": ["payment_method", "table_number"],
        "timestamp_column": "order_date"
      }
    },
    "suggested_kpis": [
      {"name": "Günlük Satışlar", "calculation": "SUM(total_amount)"}
    ]
  },
  "unclear_fields": []
}
```

---

### Adım 3: Model Eğitimi

```bash
curl -X POST http://localhost:8002/train \
  -H "Content-Type: application/json" \
  -d '{
    "company_id": "test-company-123",
    "db_connection": {
      "type": "postgresql",
      "host": "your-db-host",
      "port": 5432,
      "database": "cafe_db",
      "username": "readonly_user",
      "password": "password"
    },
    "semantic_schema": {
      "tables": {
        "orders": {
          "target_columns": ["total_amount"],
          "timestamp_column": "order_date"
        }
      }
    }
  }'
```

**Beklenen Yanıt:**
```json
{
  "company_id": "test-company-123",
  "status": "training_started",
  "message": "Model training started in background",
  "task_id": "test-company-123"
}
```

**Eğitim Durumunu Kontrol Et:**
```bash
curl http://localhost:8002/train/status/test-company-123
```

---

### Adım 4: Real-time Tahmin

```bash
curl -X POST http://localhost:8002/predict \
  -H "Content-Type: application/json" \
  -d '{
    "company_id": "test-company-123",
    "table_name": "orders",
    "data": {
      "customer_id": 123,
      "table_number": 5,
      "payment_method": "credit_card"
    }
  }'
```

**Beklenen Yanıt:**
```json
{
  "company_id": "test-company-123",
  "table_name": "orders",
  "predictions": {
    "regression": {
      "total_amount": {
        "predicted_value": 142.50,
        "target": "total_amount"
      }
    },
    "anomaly": {
      "is_anomaly": false,
      "anomaly_score": -0.15
    }
  },
  "status": "success"
}
```

---

### Adım 5: WebSocket Bağlantısı (Frontend)

```javascript
// React/JavaScript
const ws = new WebSocket('ws://localhost:8000/ws/company/test-company-123/');

ws.onopen = () => {
    console.log('WebSocket connected');
};

ws.onmessage = (event) => {
    const data = JSON.parse(event.data);
    
    if (data.type === 'realtime_update') {
        console.log('New data:', data.data);
        // Grafik güncelle
        updateChart(data.data);
    }
};

// Keep-alive
setInterval(() => {
    ws.send(JSON.stringify({ type: 'ping' }));
}, 30000);
```

---

## 📊 VERİ AKIŞI

```
1. Company DB → Yeni kayıt
     ↓
2. Celery CDC Task (30 saniyede bir poll)
     ↓
3. RabbitMQ (data.realtime exchange)
     ↓
4. Django Consumer:
     → PyCaret Engine'e POST /predict
     → Tahmin al
     → Metrikleri hesapla
     ↓
5. Channel Layer → WebSocket
     ↓
6. Frontend: Chart güncellenir (REAL-TIME!)
```

---

## 🗂️ MODEL STORAGE

```
/app/models/
├── test-company-123/
│   ├── orders_total_amount_regression.pkl
│   ├── orders_anomaly.pkl
│   └── metadata.json
```

Docker volume'de persist edilir: `pycaret.models.volume`

---

## 🔧 TROUBLESHOOTING

### Schema Analyzer bağlanamıyor

```bash
# Container içine gir
docker exec -it schema.analyzer.container bash

# Python ile test et
python3 -c "from sqlalchemy import create_engine; engine = create_engine('postgresql://...'); engine.connect()"
```

### PyCaret eğitim çok yavaş

```bash
# CPU/Memory kullanımını kontrol et
docker stats pycaret.engine.container

# Log seviyesini düşür
LOG_LEVEL=WARNING
```

### WebSocket bağlanamıyor

```bash
# Redis'in çalıştığını kontrol et
docker exec -it redis.db.basket.container redis-cli ping
# Beklenen: PONG

# Django Channels config kontrol et
docker exec -it django.ai.container python manage.py shell
>>> from channels.layers import get_channel_layer
>>> channel_layer = get_channel_layer()
>>> channel_layer
```

---

## 📝 SONRAKİ ADIMLAR

1. ✅ **Gerçek DB'ye bağla** - Test verisi ile schema analizi yap
2. ✅ **Model eğit** - PyCaret otomatik eğitecek
3. ✅ **Frontend WebSocket entegrasyonu** - Real-time chart updates
4. ⏳ **Admin Panel** - Schema onay UI'ı
5. ⏳ **Text-to-SQL** - Soru-cevap özelliği (AI Service 2 - qa)

---

## 🎯 ÖZELLİKLER

✅ **Auto Schema Discovery** - LLM ile otomatik anlama  
✅ **Zero-config AutoML** - PyCaret otomatik model seçimi  
✅ **Real-time Streaming** - WebSocket push notifications  
✅ **Multi-tenant** - Her şirket için ayrı model  
✅ **Anomaly Detection** - Anormal veri tespiti  
✅ **Docker-ready** - Tek komutla deploy  
✅ **Model Persistence** - Docker volume'de güvenli saklama  

---

**Hazır! Test etmeye başlayabilirsin!** 🚀

Herhangi bir sorun olursa log'ları kontrol et:
```bash
docker-compose logs -f schema.analyzer pycaret.engine django.ai celery.worker
```
