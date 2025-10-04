# AI Service 1: Grafik Verileri İşleme Servisi

Bu klasör, grafik verileri üretecek AI servisinin yapısını içerir.

## Yapılacaklar
- [ ] AI model seçimi ve eğitimi
- [ ] REST API endpoint'leri
- [ ] Model loading ve inference logic
- [ ] Docker container yapılandırması

## Önerilen Teknolojiler
- FastAPI (Python web framework)
- PyTorch / TensorFlow
- NumPy, Pandas (veri işleme)
- Docker

## API Endpoint Örneği
```
POST /predict/graph-data
{
    "parameters": {...}
}

Response:
{
    "success": true,
    "data": {
        "graph_type": "line",
        "values": [...],
        "labels": [...]
    }
}
```
