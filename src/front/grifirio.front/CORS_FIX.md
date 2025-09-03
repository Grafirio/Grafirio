# 🚨 ACIL: CORS Sorunu Çözümü

## CORS Hatası: 
```
Access to XMLHttpRequest at 'http://localhost:8080/realms/master/protocol/openid-connect/token' 
from origin 'http://localhost:59264' has been blocked by CORS policy
```

## ✅ Keycloak Admin Console'da DERHAL Yapılması Gerekenler:

### 1. Web Origins (EN ÖNEMLİSİ!)

**Web Origins** bölümüne şunları ekleyin (her satıra ayrı ayrı):

```
http://localhost:59266
http://localhost:59265  
http://localhost:59264
http://localhost:3000
*
+
```

**Not:** `*` veya `+` en son seçenek olarak ekleyebilirsiniz (tüm originlere izin verir)

### 2. Valid Redirect URIs

```
http://localhost:59266/*
http://localhost:59266
http://localhost:59265/*
http://localhost:59265
http://localhost:59264/*
http://localhost:59264
```

### 3. Root URL ve Home URL

```
Root URL: http://localhost:59266
Home URL: http://localhost:59266  
Admin URL: http://localhost:59266
```

### 4. ⚠️ CLIENT AUTHENTICATION'I KAPATIN!

**Capability config** sekmesinde:
- `Client authentication` → **OFF** (Bu çok önemli!)

## 🔄 Test Adımları:

1. Yukarıdaki ayarları Keycloak'ta yapın
2. **Save** butonuna basın  
3. Tarayıcıyı tamamen kapatıp açın
4. `http://localhost:59266` adresine gidin
5. Console'da yeni port bilgilerini kontrol edin
6. "Keycloak ile Giriş Yap" butonunu test edin

## 🔍 Debug Bilgileri:

Console'da şu bilgileri göreceksiniz:
```
Keycloak Configuration: {
  url: 'http://localhost:8080',
  realm: 'master', 
  clientId: 'Grifirio',
  currentOrigin: 'http://localhost:59266',
  currentPort: '59266'
}
```

Eğer hala CORS hatası alırsanız, Keycloak'ın **master realm**'inin **Realm Settings** → **Security Defenses** → **Headers** bölümünde:

- `Content-Security-Policy`: boş bırakın veya `frame-src 'self'; frame-ancestors 'self'; object-src 'none';`
- `X-Frame-Options`: `SAMEORIGIN`

---

**Bu ayarları yaptıktan sonra problem çözülmelidir!** 🚀