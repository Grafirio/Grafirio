# Yönetim merkezi

ProjectAdmin mevcut React uygulaması ve API sözleşmeleri korunarak altı bölüme ayrılır:
Genel Bakış, Sorgu Kalitesi, AI & Benchmark, Log & Telemetri, Firma Yönetimi ve Servis Yönetimi.
Eski sayfa yolları korunur; `/` Genel Bakış ekranıdır.

## Veri ve kapsam

- Genel Bakış mevcut Ops durum/audit, teşhis özeti ve benchmark okuma uçlarını kullanır.
  Kaynaklar bağımsız yüklenir; bir kaynak erişilemiyorsa diğer veriler gösterilir.
  Açılışta ve Yenile ile güncellenir. Eksik veya 30 saniyeden eski servis yoklaması sağlıklı kabul edilmez.
- Sorgu özeti son 7 gündür. İşlem başarısı, sonuç doğruluğu değildir.
  Hata görülen firma sayısı, firma kimliği bilinen başarısız sorguların firma kırılımından hesaplanır.
- Öncelikli Sorunlar, tekrarlayan hata grupları ve mevcut açık inceleme adaylarından en fazla beş sinyal gösterir.
  Gruplar ve adaylar örtüşebilir; sayıları toplanmaz. Sinyal, doğrulanmış kök neden veya ayrı bir incident kaydı değildir.
- Sorgu tablosu ve filtreler korunur. Ayrıntılı kırılımlar, dönem karşılaştırması ve diğer adaylar Analiz sekmesindedir.
  Dashboard ve firma bağlantıları `days`, `status`, `errorCode`, `companyId` filtreleriyle açılabilir; `id` ayrıntı bağlantısı korunur.
- AI karşılaştırması için baz koşu seçilebilir. Ortam/veri seti farkları ayrıca incelenmelidir.
- Canlı telemetri mevcut süreç içi tamponları kullanır; kalıcı arşiv sonraki PR ile eklenir.
- Dashboard yönetim geçmişi Ops kayıtlarıdır; bütün servislerin ortak audit arşivi değildir.

## Yönetim işlemleri

Abonelik iptali, firma üyeliğini kaldırma, servis durdurma/yeniden başlatma,
altyapıyı ayağa kaldırma, auto-heal ve teşhis/arşiv saklama ayarlarını değiştirme arayüzde etki açıklamasıyla onay ister.
Onay penceresi klavye odağını sınırlar; Escape/Vazgeç isteği göndermez ve odağı önceki kontrole döndürür.
Bu, ikinci kişi onayı veya sunucuda ayrı bir onay iş akışı değildir. Mevcut sunucu yetkilendirmesi korunur.

Ops servis kontrolünde geçersiz hedef/işlem, kapalı Docker ve eksik konteyner dahil işlem öncesi hatalar da
kim, işlem, hedef, sonuç ve hata bilgisiyle mevcut audit dosyasına yazılır.
Firma üyelik ve abonelik geçmişleri mevcut Identity kayıtlarında; teşhis işlemleri mevcut yönetim olaylarında kalır.

## Doğrulama

ProjectAdmin build ve mevcut sorgu yükleme testleri; Ops API sentetik testleri; tarayıcıda erişim sınırları,
filtre/sayfalama, kısmi kaynak kesintisi, onay/iptal/odak davranışı ve masaüstü/dar ekran kontrolleri kullanılır.
Tarayıcı fixture'ları dış origin isteklerini engeller. Canlı Docker, müşteri verisi ve model çağrısı gerekmez.
