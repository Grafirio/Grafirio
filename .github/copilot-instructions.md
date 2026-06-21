# Grafirio — Copilot Talimatları

Bu dosyadaki kurallar **her zaman aktif**tir ve workspace genelinde geçerlidir.

---

## 🚨 1. ONAYLANDI Kuralı (Esnetilemez)

**Hiçbir değişiklik / komut / dosya işlemi onay olmadan yapılmaz.**

Akış her zaman şu sırada işler:

1. Kullanıcının isteğini al.
2. **Analiz** yap (gerekirse salt-okuma araçlarla: `read_file`, `grep_search`, `file_search`, `list_dir`, `semantic_search`).
3. Yapılacakların **planını** ve dosya listesini sun.
4. Kullanıcının **"ONAYLANDI"** yazmasını bekle.
5. Onay geldikten sonra uygula.

**Kurallar:**

- "Yap", "başla", "devam et", "hadi", "tamam" gibi ifadeler **ONAYLANDI sayılmaz**. Sadece tam olarak `ONAYLANDI` kelimesi geçerlidir.
- Kullanıcı "her seferinde sormana gerek yok" dese bile bu kural esnetilmez.
- İstisna: salt-okuma keşif işlemleri (dosya/kod okuma, arama, listeleme, hata görüntüleme) onay gerektirmez.
- Onay gerektiren işlemler: dosya oluşturma, dosya silme, dosya düzenleme, terminal komutu çalıştırma (read-only komutlar hariç), paket kurulumu, migration, git işlemleri, MCP üzerinden yazma işlemleri.

---

## 2. İletişim Dili

- **Sohbet, açıklama, analiz, plan** → Türkçe.
- **Kod, sınıf adları, değişken adları, fonksiyon adları, dosya adları** → İngilizce.
- **Commit mesajları, PR başlıkları, branch adları** → İngilizce.
- **Swagger / XML doc açıklamaları, log mesajları** → İngilizce.
- **Kod içi yorumlar** → İngilizce.

---

## 3. Referans Verme Kuralı

Bir metot, sınıf, fonksiyon, dosya veya işlemden bahsederken **konumu mutlaka markdown link** olarak verilir.

**Doğru:**
> `CreateOrderAsync()` metodu [src/services/Grafirio.Commerce.Api/Modules/Orders/OrderService.cs](src/services/Grafirio.Commerce.Api/Modules/Orders/OrderService.cs#L42) içinde tanımlı.

**Yanlış:**
> `CreateOrderAsync()` metodu OrderService.cs içinde.

---

## 4. Clean Code Prensipleri (Tüm Diller)

- **Anlamlı isim**: Kısaltma yok. `usr` değil `user`, `qty` değil `quantity`.
- **Tek sorumluluk**: Bir fonksiyon / sınıf / dosya bir iş yapar.
- **DRY**: Tekrar eden kod ortak yere taşınır.
- **KISS**: En sade çözüm seçilir.
- **YAGNI**: İhtiyaç olmadan soyutlama, feature flag, fallback eklenmez.
- **Magic number / string yasak**: Sabitler `const` veya `enum` olarak tanımlanır.
- **Erken return**: İç içe `if` yerine guard clause kullanılır.
- **Ölü kod temizlenir**: Kullanılmayan import, değişken, fonksiyon silinir.
- **Yorum politikası**: Sadece "neden" yazılır, "ne" yazılmaz. Tek satır, kısa. Kodun zaten söylediği şeyi tekrar etme.

---

## 5. Dosya / Klasör Hijyeni

- Gereksiz markdown / dokümantasyon dosyası **oluşturulmaz**. Sadece kullanıcı açıkça isterse oluşturulur.
- Mevcut klasör yapısı korunur. Yeniden organize etmeden önce sorulur.
- Geçici / deneme dosyaları (`test.cs`, `temp.js`, `Untitled-1`) commit edilmez.

---

## 6. Hassas Veri Yönetimi (Tüm Servisler)

**Asla** kaynak kodda, `appsettings.json` içinde veya commit'lerde tutulmaz:

- Veritabanı parolası
- API anahtarı / token
- JWT secret
- Connection string parolası
- Keycloak client secret
- SMTP parolası
- Bulut sağlayıcı (Azure, AWS) anahtarları

**Doğru yerler:**

| Ortam | Yöntem |
|---|---|
| .NET Development | `dotnet user-secrets` |
| .NET Production / Docker | Environment variable |
| Frontend Development | `.env.local` (git'e gitmez) |
| Frontend Production | Build-time env veya runtime config |
| Production / Cloud | Azure Key Vault / AWS Secrets Manager |

`appsettings.json` dosyalarında sadece **şablon / dummy** değer bulunur:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=Grafirio;User Id=__SET_VIA_ENV__;Password=__SET_VIA_ENV__;"
  }
}
```

`.env.example` dosyası repo'da bulunabilir; `.env` dosyaları `.gitignore` içinde olmalıdır.

---

## 7. Davranış Notları

- Mevcut framework / kütüphane garantilerine güven. Sınır olmayan yerlerde `null`, undefined, range kontrolü ekleme.
- Backwards compatibility shim'i eklenmez; kod doğrudan değiştirilir.
- Hataları susturan `try/catch` yok; ya işle ya logla ya yukarı fırlat.
- Test, lint, build komutu çalıştırmadan önce de plan sunulur ve **ONAYLANDI** beklenir.
