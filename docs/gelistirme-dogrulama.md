# Geliştirme ortamı doğrulaması — 17 Ekim 2026

Bu rapor yerel Windows çalışma ağacında yapılan kontrollerdir; GitHub koşusu, canlı dağıtım veya üretim doğrulaması değildir. Çalışma ağacındaki mevcut kullanıcı değişiklikleri testlere dahildi; bu değişiklikler altyapı commit'ine dahil edilmez.

| Kontrol | Sonuç |
| --- | --- |
| .NET 8 birim/sözleşme projesi, Release | 1.305 test başarılı, başarısız/atlanan yok |
| Python 3.12, dosya başına ayrı süreç | 164 pytest testi ve 114 alt kontrol başarılı; ayrıca query_spec/data_port script kontrolleri başarılı |
| Firma paneli JavaScript + desktop auth | 113 test başarılı; modül taklidi seçeneğiyle |
| ProjectAdmin JavaScript | 4 test başarılı |
| Ops agent JavaScript | 17 test başarılı |
| Kalite kapsam seçimi | 6 test başarılı |
| ProjectAdmin tarayıcı | 4 senaryo başarılı, sentetik kimlik/veri ile |
| Üç React uygulamasının üretim derlemesi | Başarılı; firma paneline sentetik HTTPS ayarı sağlandı |
| Worktree hazırlayıcı | Farklı proje/parola/port, mevcut dosyayı koruma, dolu portu reddetme başarılı |
| Test Compose yapılandırması | Konteyner/volume isim kapsamı ve loopback portları doğrulandı; konteyner başlatılmadı |
| Skill doğrulayıcısı | Üç skill başarılı |
| Workflow denetimi | Kalite ve dağıtım dosyaları actionlint ile başarılı |

Python kontrolü mevcut iki kusuru ortaya çıkardı ve dar kapsamlı düzeltmeler yapıldı:

- Sabit seri Holt ile modellendiğinde makine hassasiyetindeki artıklar yanlış sapma işareti üretebiliyordu. Sabit seri tam sabit beklentiyle işleniyor; sıfır/negatif/büyük değer ve farklı seri uzunlukları aynı regresyon testinde doğrulandı.
- Bazı testlerin geçici modül yüklemesi saat taklidinin farklı bir modül nesnesine uygulanmasına yol açıyordu. Tarih modülü taklit kapsamından önce yüklenip aynı nesne üzerinden yamalandı; veri/tarih beklentileri gevşetilmedi.

Sınırlar: Tam PyCaret eğitim/tahmin ortamı, müşteri veritabanı, Bridge entegrasyon ortamı, gerçek OIDC ve canlı model çağrıları çalıştırılmadı. Sonuç benchmark'ı için ayrıca onaylanmış READ_ONLY test veritabanı gerekir. Verimlilik pilotu henüz ölçülmedi.

ProjectAdmin lint mevcut 109 hata nedeniyle geçmiyor (çoğunluğu API tarafının CommonJS ortamı olarak tanımlanmaması). Paket araçları mevcut bağımlılıklarda güvenlik bildirimleri de verdi; bu çalışma bağımlılık yükseltmesi yapmadı. Ayrıntılı test raporları yerel `artifacts/quality` altında, tarayıcı çıktıları uygulamanın `test-results` klasöründedir; bu raporlar Git dışında kalır.
