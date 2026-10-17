---
name: grafirio-akis-anlat
description: Grafirio deposunda bir metodun veya kullanıcı işleminin çağrı zincirini, karar dallarını ve hata yollarını kod kanıtlarıyla açıkla. Kod öğrenme ve akış inceleme isteklerinde kullan.
---

# Grafirio akışını anlat

Depo kökündeki `AGENTS.md` ve ihtiyaca göre `docs/gelistirme-rehberi.md` başlangıç haritasını oku. Bu skill Grafirio deposuna özeldir.

İstenen sembolün tanımını, çağıranlarını ve somut uygulamasını bul. Arayüzden gidiyorsan Program.cs içindeki DI kaydını doğrula. Öğrenme isteği tek başına kod değiştirme talebi değildir.

UI'dan başlayan işlerde istemci URL'si → gateway rotası → endpoint → servis → Python veya veri kaynağı sınırını gerçekten izleyebildiğin yere kadar takip et. Her katmanın bütün akışlarda bulunduğunu varsayma. Bridge/doğrudan erişim kararını ilgili fabrika ve politikadan çıkar.

Kullanıcıya girdinin nasıl dönüştüğünü ve çıktının nasıl oluştuğunu anlat. Ana yolun ardından davranışı değiştiren koşulları açıkla: firma/bağlantı yetkisi, eksik bağlam, yeniden deneme, zaman aşımı, iptal, iş durumu ve çevrimdışı bağlantı. Yalnızca ilgili olanları dahil et.

Gerçek dosya/metot bağlantıları ver. Koddan çıkarımı, testle doğrulanmış davranışı ve henüz görülmeyen çalışma zamanı bilgisini ayır. Uygunsa kısa sıralı örnek veya Mermaid kullan; büyük bir diyagram için kanıtlanmamış ok ekleme.

İlgili testlerin hangi dalı doğruladığını belirt. Bulamadığın testi varmış gibi anlatma. Kullanıcı risk analizi istiyorsa riskleri tetikleyici koşul ve kullanıcıya etkisiyle ilişkilendir.
