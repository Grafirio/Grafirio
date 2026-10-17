# Grafirio çalışma rehberi

Grafirio; React panelleri, .NET servisleri, Python analiz motoru ve müşteri ağında çalışan Bridge içerir. Başlangıç haritası ve test seçimi için `docs/gelistirme-rehberi.md` dosyasını okuyun. Harita bir başlangıç noktasıdır; davranışı güncel çağrı noktalarından doğrulayın.

## Projeye özgü kararlar

- Kullanıcıya Türkçe açıklayın. Akış anlatırken gerçek dosya/metot bağlantıları, önemli dallar ve hata yollarını gösterin; statik incelemeyi çalıştırılmış davranış gibi sunmayın.
- README'deki servis/port bilgilerini güncel Program.cs, gateway yapılandırması ve çalıştırma dosyalarıyla karşılaştırın; eski Catalog/Basket anlatımını mevcut Commerce yapısına doğrudan uygulamayın.
- Veri erişiminde mevcut `IDataSourceFactory` ve QueryPolicy yaklaşımını inceleyin. Yeni doğrudan SQL bağlantısı veya ayrı sorgu doğrulayıcısı eklemeden önce mevcut sınırları kullanın.
- Firma/bağlantı sahipliği, platform yöneticisi yetkisi, Bridge kimliği ve Python iç servis kimliğini farklı güven sınırları olarak değerlendirin.
- `shared-packages.ref` değişikliğinde `shared-packages.md` ve bütün tüketici servislerin CI tetiklerini kontrol edin. Yalnızca bir servisin derlenmesi uyumluluğu kanıtlamaz.
- Model çevirisi, SQL politika uygunluğu, çalıştırma başarısı ve sonuç doğruluğunu ayrı değerlendirin. Ölçüm kapsamı için `docs/query-diagnostics.md` ve `docs/result-validation.md` kullanın.
- Testi değişen davranışa göre seçin; çalıştırılamayan kontrolün nedenini belirtin. Canlı teşhis/model çağrıları ve benchmark yayınları yerel birim testi değildir; ilgili araç ve ortam koşullarını önce okuyun.
- Tekrar eden kod için önce mevcut arayüz, yardımcı ve benzer uygulamayı bulun. Yeni ortak soyutlamayı gerçek tekrar ve davranış ortaklığı varsa çıkarın; yalnızca görünüşü benzer kodları birleştirmeyin.
- Kütüphane API'si belirsizse önce projedeki sürümü belirleyin, sonra Context7 veya resmi belge kullanın. Context7'ye gizli değer veya özel kaynak kodu göndermeyin.
- Veri kaybı yaratabilecek migration/backfill/reset işlemlerini kod incelemesinden ayırın; görev açıkça yetkilendirmedikçe çalıştırmayın. Testlerde müşteri verisi yerine sentetik fixture kullanın. Yazılı talimatın veritabanı yetki sınırı olmadığını unutmayın.
- Bağımsız çalışma için mevcut uygun worktree'yi tercih edin. `docker-compose.yml` sabit port/konteyner adları içerir; farklı worktree'de ikinci kez başlatmak izolasyon sağlamaz. Yerel test ortamı için `docs/calisma-ortami.md` kullanın.
- Doğrulama planı: `node tools/quality/plan.mjs`; ilgili otomatik kontroller `.github/workflows/quality.yml` içindedir. Salt belge değişikliği için uygulama testlerini zorunlu tutmayın.

## Odaklı iş akışları

Bu depoda `.agents/skills/` altında üç skill bulunur. Oturumda otomatik listelenmiyorlarsa ilgili `SKILL.md` dosyasını doğrudan okuyarak uygulayın:

- Akış/metot öğrenme: `.agents/skills/grafirio-akis-anlat/SKILL.md`
- Hata teşhisi ve düzeltme: `.agents/skills/grafirio-hata-coz/SKILL.md`
- Değişiklik etkisi, inceleme ve test seçimi: `.agents/skills/grafirio-degisiklik-kontrol/SKILL.md`
