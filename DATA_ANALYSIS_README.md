#  Grafirio Data Analysis API

##  Özet
Django AI servisi ile entegre çalışan veri analiz API'si. Tüm analiz sorguları Django AI'ya yönlendirilir.

##  Mimari

\\\
Frontend  .NET API  RabbitMQ  Django AI  NLP Model  SQL Generator  Database
                                    
                              Response  Charts & Answer
\\\

##  Kullanım

### 1. Soru Sorma
\\\http
POST http://localhost:5221/api/ai/reports/ask-question
Content-Type: application/json

{
  \"requestId\": \"uuid\",
  \"question\": \"Son 15 günün en çok satan ürününü göster\",
  \"database\": \"GrafirioECommerce\",
  \"tables\": [\"Products\", \"Orders\", \"OrderItems\"]
}
\\\

**Response:**
\\\json
{
  \"success\": true,
  \"question\": \"...\",
  \"answer\": \" Sorunuz Django AI tarafından analiz ediliyor...\",
  \"charts\": [...],
  \"answeredAt\": \"2025-10-14T...\"
}
\\\

### 2. Rapor Oluşturma
\\\http
POST http://localhost:5221/api/ai/reports/generate
Content-Type: application/json

{
  \"requestId\": \"uuid\",
  \"reportType\": \"trend-analysis\",
  \"database\": \"GrafirioECommerce\",
  \"tables\": [\"Orders\"]
}
\\\

##  Servisler

### API Endpoints
- \POST /api/ai/reports/ask-question\ - Soru-cevap
- \POST /api/ai/reports/generate\ - Rapor oluşturma
- \GET /health\ - Sağlık kontrolü

### Dependencies
- **RabbitMQ**: Mesaj kuyruğu (localhost:5672)
- **Django AI**: NLP + SQL generator (localhost:8000)
- **SQL Server**: Test veritabanı (localhost:1434)

##  Kurulum

\\\ash
# 1. Docker servisleri başlat
docker-compose up -d rabbitmq django.ai celery.worker

# 2. .NET API'yi çalıştır
cd src/services/Grafirio.DataAnalysis.Api
dotnet run

# 3. Frontend'i başlat
cd src/front/grifirio.front
npm run dev
\\\

##  Test

\\\ash
# Health check
curl http://localhost:5221/health

# Soru test
curl -X POST http://localhost:5221/api/ai/reports/ask-question \
  -H \"Content-Type: application/json\" \
  -d '{
    \"requestId\": \"test-123\",
    \"question\": \"Kaç tane ürün var?\",
    \"database\": \"GrafirioECommerce\",
    \"tables\": [\"Products\"]
  }'
\\\

##  İş Akışı

1. **Frontend**  Kullanıcı soru sorar
2. **.NET API**  Soruyu RabbitMQ'ya gönderir
3. **Django AI**  RabbitMQ'dan alır, NLP ile analiz eder
4. **SQL Generator**  Veritabanı sorgusu oluşturur
5. **Database**  Sorgu çalıştırılır
6. **Chart Generator**  Grafik verisi hazırlanır
7. **Response**  RabbitMQ üzerinden döner
8. **Frontend**  Sonuç gösterilir

##  Notlar

-  **Tüm sorular Django AI'ya gidiyor** - Manuel keyword matching kaldırıldı
-  **NLP destekli** - Türkçe/İngilizce doğal dil desteği
-  **Scalable** - RabbitMQ ile async işleme
-  **Flexible** - Yeni soru tipleri otomatik destekleniyor

##  Troubleshooting

**API çalışmıyor:**
\\\ash
# RabbitMQ kontrolü
docker ps | grep rabbitmq

# Django AI kontrolü
curl http://localhost:8000/api/health/
\\\

**Cevap gelmiyor:**
- RabbitMQ queue'larını kontrol et: http://localhost:15672 (guest/guest123)
- Django AI loglarına bak: \docker-compose logs -f django.ai\
- Celery worker çalışıyor mu: \docker-compose logs -f celery.worker\

---

**Son Güncelleme:** 2025-10-14  
**Durum:**  Production Ready
