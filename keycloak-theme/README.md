# 🎨 Grifirio Keycloak Tema Kurulum Rehberi

## 📁 Tema Dosya Yapısı

Bu tema Tabler.io ile uyumlu, modern ve şık bir Keycloak login sayfası sağlar.

```
keycloak-theme/
└── grifirio/
    └── login/
        ├── theme.properties
        ├── login.ftl
        └── resources/
            └── css/
                └── grifirio.css
```

## 🚀 Kurulum Adımları

### 1. Tema Dosyalarını Keycloak'a Kopyalama

**Seçenek A: Development (Kolay)**
```bash
# Keycloak klasörünüze gidin (örnek yol)
cd C:\keycloak-23.0.0\themes

# Grifirio klasörünü buraya kopyalayın
# Sonuç: C:\keycloak-23.0.0\themes\grifirio\
```

**Seçenek B: Docker (eğer Docker kullanıyorsanız)**
```bash
# Tema klasörünü container'a mount edin
docker run -p 8080:8080 \
  -v ./keycloak-theme:/opt/keycloak/themes \
  quay.io/keycloak/keycloak:latest
```

### 2. Keycloak'ı Yeniden Başlatma

```bash
# Development mode
./bin/kc.sh start-dev

# Veya production mode  
./bin/kc.sh start
```

### 3. Admin Console'da Tema Aktifleştirme

1. Keycloak Admin Console'a gidin: `http://localhost:8080`
2. **Realm Settings** → **Themes** sekmesine gidin
3. **Login Theme** dropdown'ından **"grifirio"** seçin
4. **Save** butonuna basın

### 4. Test Etme

1. `http://localhost:59266` adresine gidin
2. "Keycloak ile Giriş Yap" butonuna tıklayın
3. Artık modern Tabler.io tasarımında login sayfasını göreceksiniz!

## 🎨 Tema Özellikleri

- ✅ Tabler.io CSS framework'ü
- ✅ Modern ve temiz tasarım
- ✅ Responsive (mobil uyumlu)
- ✅ Grifirio branding
- ✅ Custom renkler ve tipografi
- ✅ Hata mesajları styling
- ✅ Social login desteği
- ✅ Accessibility özellikleri

## 🔧 Özelleştirme

`resources/css/grifirio.css` dosyasını düzenleyerek:
- Renkleri değiştirebilirsiniz
- Logo ekleyebilirsiniz  
- Layout'u ayarlayabilirsiniz
- Animasyonlar ekleyebilirsiniz

## 🚨 Sorun Giderme

**Tema görünmüyorsa:**
1. Keycloak'ı yeniden başlattığınızdan emin olun
2. Tema dosyalarının doğru yerde olduğunu kontrol edin
3. Browser cache'ini temizleyin
4. Console'da hata olup olmadığını kontrol edin

**Stil değişiklikleri görünmüyorsa:**
1. Browser'ın cache'ini temizleyin (Ctrl+F5)
2. CSS dosyasında syntax hatası olup olmadığını kontrol edin

---

**Artık profesyonel görünümlü bir login sayfanız var!** 🎉