# 🎛️ Grafirio Project Admin - Service Manager

## 📋 Genel Bakış

Grafirio Project Admin, tüm mikroservisleri, AI servislerini, veritabanlarını ve altyapı bileşenlerini **tek bir yerden** yönetmenizi sağlayan kapsamlı bir admin dashboard'udur.

## ✨ Özellikler

### 📊 Gerçek Zamanlı İzleme
- **15 farklı servisi** aynı anda izleyin
- Otomatik sağlık kontrolleri (30 saniyede bir)
- Renkli durum göstergeleri (Online, Offline, Error, Timeout, Unknown)
- Canlı istatistikler (Toplam, Online, Offline, Hatalı, Bilinmeyen)

### 🔍 Kategori Bazlı Filtreleme
- **AI Services**: Schema Analyzer, PyCaret Engine, Django AI, Celery Worker
- **.NET Microservices**: Identity API, Catalog API, Basket API
- **Infrastructure**: RabbitMQ, Redis, MongoDB (x2), SQL Server, Keycloak, PostgreSQL
- **Frontend**: React Frontend

### 📋 Detaylı Servis Bilgileri
- Servis adı ve açıklaması
- Port numaraları
- Servis tipi (Docker, .NET, NPM)
- Erişim URL'leri
- Kimlik bilgileri (credentials)

### 🎮 Servis Kontrolleri
- ▶️ Başlat (Start) - Servisi başlatır
- ⏹️ Durdur (Stop) - Servisi durdurur
- 🔄 Yeniden Başlat (Restart) - Servisi yeniden başlatır
- 🔑 Credentials - Giriş bilgilerini gösterir
- 🔗 URL Aç - Servis yönetim UI'ını açar

## 🚀 Hızlı Başlangıç

### Projeyi Başlatma
```powershell
cd "d:\Projeler\Grifirio\src\front\admins\grifirio.projectadmin"
npm run dev
```

### Erişim
```
http://localhost:59666
```

## 📊 İzlenen Servisler

**Toplam:** 15 servis
- 🤖 AI Services: 4
- 🔧 .NET Microservices: 3
- 🗄️ Infrastructure: 7
- 🎨 Frontend: 1

## 🎯 Kullanım

1. **Dashboard Açın:** http://localhost:59666
2. **Servisleri İzleyin:** Otomatik health check çalışıyor
3. **Filtreleyin:** Kategori dropdown'dan seçim yapın
4. **Yönetin:** ▶️ ⏹️ 🔄 butonlarıyla kontrol edin (backend API gerekli)
5. **Credentials:** 🔑 butonuna tıklayarak giriş bilgilerini görün
6. **URL Aç:** 🔗 ile management UI'ları açın

## 🏗️ Proje Yapısı

```
grifirio.projectadmin/
├── src/
│   ├── components/
│   │   ├── ServiceDashboard.jsx    # Ana dashboard
│   │   └── ServiceDashboard.css    # Stiller
│   ├── services/
│   │   └── serviceManager.js       # API & servis tanımları
│   ├── App.jsx
│   └── main.jsx
├── package.json
└── vite.config.js (Port: 59666)
```

## 🔧 Teknik Detaylar

- **React 19.1.1** + **Vite 7.1.4**
- **Axios** - HTTP client
- Auto-refresh: 30 saniye
- Responsive design
- Modern gradient UI

## 🔗 Linkler

- **Service Manager:** http://localhost:59666 ⭐
- **Ana Frontend:** http://localhost:59264
- **Test Sayfası:** http://localhost:59264/test-ai-services.html
- **AI Dashboard:** http://localhost:59264/ai-dashboard-public

## 📝 Not

Docker container başlatma/durdurma için backend API entegrasyonu gereklidir. Şu anda sadece monitoring ve health check yapılmaktadır.

---

**🎉 Tüm servisleri tek ekrandan yönetin!**
