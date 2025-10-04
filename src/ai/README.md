# Grafirio AI Service Integration

Bu klasör, Grafirio mikroservis mimarisinde AI entegrasyonu için gerekli tüm yapıyı içerir.

## 🏗️ Mimari Genel Bakış

```
Frontend (React)
    ↓ HTTP
Gateway (YARP) 
    ↓
Django REST API
    ↓
RabbitMQ (MassTransit)
    ↓
Celery Worker → AI Service 1 (Grafik Verileri)
             → AI Service 2 (Soru-Cevap)
    ↓
RabbitMQ Response
    ↓
Django Consumer → WebSocket → Frontend
```

## 📁 Klasör Yapısı

```
src/ai/
├── grafirio-ai-service/          # Django REST API + RabbitMQ + WebSocket
│   ├── api/                      # REST endpoints
│   ├── messaging/                # RabbitMQ publishers & consumers
│   ├── tasks/                    # Celery tasks
│   ├── config/                   # Django settings
│   └── requirements.txt
│
├── ai-service-1-graph/           # AI Grafik Verisi Servisi (Boş - Doldurulacak)
└── ai-service-2-qa/              # AI Soru-Cevap Servisi (Boş - Doldurulacak)
```

## 🚀 Kurulum

### 1. RabbitMQ'yu Başlat

```bash
docker-compose up -d rabbitmq
```

RabbitMQ Management UI: http://localhost:15672  
Kullanıcı: `guest` / Şifre: `.env` dosyasındaki `RABBITMQ_PASSWORD`

### 2. Django Servisini Ayağa Kaldır

```bash
# Lokal geliştirme için
cd src/ai/grafirio-ai-service

# Virtual environment oluştur
python -m venv venv
source venv/bin/activate  # Windows: venv\Scripts\activate

# Paketleri yükle
pip install -r requirements.txt

# .env dosyasını oluştur
cp .env.example .env

# Migrate
python manage.py migrate

# Sunucuyu başlat (ASGI - WebSocket desteği)
daphne -b 0.0.0.0 -p 8000 config.asgi:application
```

### 3. RabbitMQ Consumer'ı Başlat

Yeni bir terminal:
```bash
python manage.py start_ai_consumer
```

### 4. Celery Worker'ı Başlat

Yeni bir terminal:
```bash
celery -A config worker --loglevel=info
```

### 5. Docker ile Tüm Stack'i Ayağa Kaldır

```bash
docker-compose up -d django.ai celery.worker
```

## 🔗 API Endpoint'leri

### 1. Grafik Verisi Talebi

```http
POST http://localhost:5000/api/ai/graph-data/
Authorization: Bearer {token}
Content-Type: application/json

{
    "user_id": "user123",
    "company_id": "company456",
    "parameters": {
        "date_range": "last_30_days",
        "metric": "sales"
    }
}
```

**Response:**
```json
{
    "request_id": "uuid",
    "status": "processing",
    "message": "Request sent to AI service. Use WebSocket to receive updates."
}
```

### 2. Soru-Cevap Talebi

```http
POST http://localhost:5000/api/ai/question/
Authorization: Bearer {token}
Content-Type: application/json

{
    "user_id": "user123",
    "company_id": "company456",
    "question": "Geçen ayki satışlar nasıldı?",
    "context": ["previous_conversation_context"]
}
```

### 3. Health Check

```http
GET http://localhost:5000/api/ai/health/
```

## 🔌 WebSocket Bağlantısı

Frontend'den WebSocket ile bağlan:

```javascript
const ws = new WebSocket('ws://localhost:8000/ws/ai/{user_id}/');

ws.onmessage = (event) => {
    const data = JSON.parse(event.data);
    
    if (data.type === 'ai_response') {
        console.log('AI Response:', data.data);
        // Grafik güncelle veya cevabı göster
    }
};

// Ping/Pong for keep-alive
setInterval(() => {
    ws.send(JSON.stringify({ type: 'ping' }));
}, 30000);
```

## 📊 .NET Servislerinde MassTransit Kullanımı

### 1. appsettings.json'a RabbitMQ Ekle

```json
{
  "RabbitMq": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest123"
  }
}
```

### 2. Program.cs'de MassTransit Ekle

```csharp
using Grafirio.Shared.MassTransit.Extensions;

var builder = WebApplication.CreateBuilder(args);

// MassTransit + RabbitMQ
builder.Services.AddGrafiiroMassTransit(builder.Configuration);

var app = builder.Build();
app.Run();
```

### 3. AI Servisine Mesaj Gönder

```csharp
using Grafirio.Shared.MassTransit.Messages.AI;
using MassTransit;

public class MyService
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MyService(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task RequestGraphData()
    {
        var request = new GraphDataRequest(
            RequestId: Guid.NewGuid(),
            UserId: "user123",
            CompanyId: "company456",
            RequestTime: DateTime.UtcNow,
            Parameters: new Dictionary<string, object> 
            {
                { "metric", "sales" }
            }
        );

        await _publishEndpoint.Publish<IGraphDataRequest>(request);
    }
}
```

## 🧪 Test

### RabbitMQ Management UI'dan Test

1. http://localhost:15672 aç
2. Queues sekmesine git
3. `django.ai.requests` queue'suna manuel mesaj gönder:

```json
{
    "request_id": "test-123",
    "user_id": "user123",
    "company_id": "company456",
    "request_time": "2025-10-04T10:00:00Z",
    "parameters": {}
}
```

4. Routing key: `ai.request.graph`
5. Celery log'larında işlemi göreceksin

## 🔧 Celery Monitoring (Flower)

```bash
celery -A config flower
```

Flower UI: http://localhost:5555

## 📝 Yapılacaklar

- [ ] AI Service 1 implementasyonu (FastAPI + Model)
- [ ] AI Service 2 implementasyonu (LLM entegrasyonu)
- [ ] Frontend WebSocket entegrasyonu
- [ ] Authentication/Authorization ekleme
- [ ] Error handling ve retry mechanism iyileştirme
- [ ] Logging ve monitoring (Prometheus, Grafana)
- [ ] Load testing

## 🐛 Sorun Giderme

### RabbitMQ'ya bağlanamıyor

```bash
# RabbitMQ container'ının çalıştığından emin ol
docker ps | grep rabbitmq

# Log'ları kontrol et
docker logs rabbitmq.container
```

### Celery task'lar çalışmıyor

```bash
# Celery worker log'larını kontrol et
# Queue'larda mesaj birikip birikmediğini RabbitMQ Management'tan kontrol et
```

### WebSocket bağlantısı kopuyor

```bash
# Redis'in çalıştığından emin ol (Channels için gerekli)
docker ps | grep redis

# Django Channel Layer'ı test et
python manage.py shell
>>> from channels.layers import get_channel_layer
>>> channel_layer = get_channel_layer()
>>> channel_layer
```

## 📚 Kaynaklar

- [Django Channels Documentation](https://channels.readthedocs.io/)
- [Celery Documentation](https://docs.celeryproject.org/)
- [RabbitMQ Tutorials](https://www.rabbitmq.com/tutorials)
- [MassTransit Documentation](https://masstransit-project.com/)
