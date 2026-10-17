---
name: grafirio-degisiklik-kontrol
description: Grafirio kod değişikliklerinin servisler arası etkisini, sözleşme risklerini ve gerekli test kapsamını incele. Değişiklik planı, kod incelemesi ve tekrar eden kod çözümü taleplerinde kullan.
---

# Grafirio değişiklik etkisini kontrol et

Depo kökündeki `AGENTS.md` ve `docs/gelistirme-rehberi.md` ile başla. İstenen değişiklik/diff kapsamını belirle; bütün çalışma ağacını kullanıcının mevcut görevi sayma.

Değişen sembol ve sözleşmelerin tüketicilerini izle. JSON alanı, null davranışı, hata kodu, endpoint yolu, iş durumu veya iptal davranışı değişiyorsa ilgili React/.NET/Python/Bridge tüketicilerini bul. Liste/özet filtrelerinin ortak davranışı gibi gizli eşleşmeleri ilgili testten doğrula.

Ortak SQL kuralları ve analiz politikası değişiyorsa API, Bridge ve Python tarafında ayrı uygulama/kopya bulunup bulunmadığını kontrol et. Yeni doğrulayıcı eklemek yerine mevcut ortak sınırları değerlendir. Firma izolasyonu ve kimlik sınırları değişiyorsa bunlara özgü negatif senaryoları dahil et.

`shared-packages.ref` veya paket sözleşmesi değiştiğinde `shared-packages.md`, tüketici servisler ve `.github/workflows/azure-containerapps-cicd.yml` dosyasını birlikte incele. Deploy yapmadan CI tetiklerinin kapsamını değerlendirebilirsin.

Yeniden kullanım istendiğinde önce mevcut benzer uygulamaları bul; değişmeyen davranış ile değişken parçaları ayır. Salt benzer görünüm ortak soyutlama için yeterli değildir. Kullanılan sürüme uygun kütüphane API'sini belirsizlik varsa Context7/resmi belgelerle doğrula; sorguya özel kod veya gizli değer koyma.

Testleri değişen davranış ve tüketicilere göre seç. Çeviri, yürütme ve sonuç doğruluğunu ayrı ölç. İnceleme isteğinde yalnızca kanıtlı, eyleme dönük bulguları dosya/satır, tetikleyici ve etkiyle ver; varsayımsal riskleri doğrulanmış hata gibi sunma. Uygulama istendiyse değişikliği tamamlayıp ilgili kontrolleri raporla; inceleme tek başına kod değişikliği gerektirmez.

EF Core migration değiştiğinde kolon silme/yeniden adlandırma, null zorunluluğu, varsayılan değer, geri dönüş ve veri dönüştürme etkisini incele. İncelemek migration çalıştırmayı gerektirmez. Transaction sınırlarını özellikle birden çok kalıcı yazma ve mesaj gönderimi arasındaki kısmi başarısızlıklarda kontrol et.

Async zincirde CancellationToken'ın HTTP, veri erişimi ve iş iptaline aktarılıp aktarılmadığını, bloklayan beklemeleri ve yinelenen isteğin etkisini incele. İptalin kullanıcıya görünmesi ile sunucu işinin gerçekten durmasını ayır.

Kod incelemesinde her bulguya önem derecesi (P0–P3), kesin dosya/satır, tetikleyici koşul ve kullanıcı etkisi ekle. Bulgusuz incelemeye hata uydurma; çalıştırılmayan testleri ve kapsam sınırını belirt. Sadece inceleme istendiğinde kodu değiştirme.
