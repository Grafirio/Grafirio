# Keycloak Ayarları - 401 Unauthorized Hatası Çözümü

## 🔧 Keycloak Admin Console'da Yapılması Gerekenler

### 1. Client Oluşturma/Düzenleme

1. Keycloak Admin Console'a gidin: `http://localhost:8080`
2. **Master** realm'ini seçin (veya kendi realm'inizi)
3. **Clients** menüsüne gidin
4. **"Grifirio"** client'ını bulun veya oluşturun

### 2. Client Ayarları (Settings Tab)

```
Client ID: Grifirio
Name: Grifirio Frontend
Description: React Frontend Application

Client Protocol: openid-connect
Access Type: public (veya confidential)
Standard Flow Enabled: ON
Implicit Flow Enabled: OFF
Direct Access Grants Enabled: ON
Service Accounts Enabled: OFF (public client ise)

Root URL: http://localhost:59265
Valid Redirect URIs: 
  - http://localhost:59265/*
  - http://localhost:59265/login/callback
  - http://localhost:59264/*  (port değişirse diye)

Base URL: http://localhost:59265
Admin URL: http://localhost:59265
Web Origins: 
  - http://localhost:59265
  - http://localhost:59264
  - +  (veya *)
```

### 3. Advanced Settings

```
Access Token Lifespan: 5 Minutes (300)
Client Session Idle: 30 Minutes
Client Session Max: 10 Hours
```

### 4. Authentication Flow Overrides (opsiyonel)

```
Browser Flow: browser
Direct Grant Flow: direct grant
```

## 🚨 Yaygın Hatalar ve Çözümleri

### 401 Unauthorized Hatası

1. **Client'ın "public" olarak ayarlandığından emin olun**
2. **Valid Redirect URIs'nin doğru olduğunu kontrol edin**
3. **Web Origins'e localhost eklendiğinden emin olun**

### CORS Hataları

Web Origins bölümüne şunları ekleyin:
- `http://localhost:59265`
- `http://localhost:59264`
- `*` (test için, production'da önerilmez)

### Token Hataları

- Direct Access Grants Enabled: **ON** olmalı
- Standard Flow Enabled: **ON** olmalı

## 🔍 Debug Bilgileri

React Console'da şu bilgileri kontrol edin:

1. **Keycloak Configuration** logunu göreceksiniz
2. **Keycloak events** loglarını takip edin
3. **Network** sekmesinde token request'lerini kontrol edin

## 📝 Test Adımları

1. Ayarları yaptıktan sonra tarayıcıyı yenileyin
2. Console'da hata olup olmadığını kontrol edin
3. "Keycloak ile Giriş Yap" butonunu test edin
4. Keycloak login sayfasına yönlendirilmeli

## 🔗 Kullanışlı Keycloak Admin URLs

- Admin Console: http://localhost:8080
- Master Realm: http://localhost:8080/admin/master/console/
- Clients: http://localhost:8080/admin/master/console/#/realms/master/clients

---

**Not:** Bu ayarları yaptıktan sonra uygulamayı yeniden başlatmanız gerekebilir.