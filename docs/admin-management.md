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
- Canlı telemetri mevcut süreç içi tamponları kullanmaya devam eder. Ayrı Kalıcı Arşiv sekmesi
  .NET, Python ve Bridge güvenli kayıtlarını instance kimliklerini koruyarak okuyabilir.
  Kaynak aktarımı ve toplayıcı ayrıca yapılandırılır; konteyner stdout/stderr toplama bu kapsamda değildir.
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

## Kalıcı telemetri ve Python/Bridge — kapsam ve kabul ölçütleri

Bu iki aşamanın kod uygulaması eklendi. 18 Ekim 2026'da arşiv PostgreSQL deposuyla canlıya kuruldu;
dört .NET servisi ve Python'un aktarımı, gateway ve yeni panel sürümü yayımlandı.
Bridge istemcisinin yeni outbox kodu yerelde doğrulandı; müşteri cihazlarına yeni Bridge sürümü
dağıtılması ayrı bir adımdır. Aşağıdaki maddeler kapsam/kabul
ölçütleridir. Uygulama, işletim sınırları, yapılandırma ve doğrulama için [kalıcı arşiv rehberi](telemetry-archive.md) kullanılır.

### 1. Kalıcı telemetri arşivi

- Önce mevcut OpenTelemetry üreticileri, dışa aktarma ayarları ve dağıtım altyapısı incelenecek;
  mevcut React ve servis API'leri korunarak uyumlu toplama/saklama yaklaşımı seçilecek.
- Log, metrik ve trace geçmişi servis yeniden başlatmalarından bağımsız saklanacak.
  Saklama süresi, hacim sınırı ve maliyet bütçesi uygulamadan önce belirlenecek.
- Servis, ortam, sürüm ve instance/replica kimlikleri korunacak. Tek instance ile tüm replikaların
  toplamı arayüzde ayrılacak; aynı olayın iki kez sayılması engellenecek.
- Panelde tarih aralığı, servis, seviye ve trace filtreleriyle geçmiş araması ve sayfalama sunulacak.
  Toplayıcı kesintisi, geciken veri ve kayıp kayıtlar görünür olacak; veri yokluğu sağlıklı durum sayılmayacak.
- Platform yöneticisi yetkisi sunucuda uygulanacak. Hassas alanlar kalıcı depoya yazılmadan maskelenecek;
  token, bağlantı sırrı, müşteri sorgu gövdesi ve ham sonuçlar varsayılan log kapsamına alınmayacak.
  Saklama ve silme ayarları onay ve audit kapsamında olacak.

Kabul ölçütleri: sentetik kayıt yeniden başlatmadan sonra bulunabilmeli; iki replikanın kayıtları
ayırt edilebilmeli; tarih filtreleri/sayfalama doğru çalışmalı; yetkisiz erişim reddedilmeli;
maskeleme, saklama sınırı ve toplayıcı kesintisi senaryoları doğrulanmalı.

### 2. Python ve Bridge loglarının merkezi görünümde birleştirilmesi

- Kalıcı toplama hattının ardından Python ve Bridge'in mevcut log/telemetri üretimi ve kimlik akışları incelenecek.
  Mevcut sağlayıcılar kullanılacak; ayrı ve tekrarlayan log altyapısı kurulmayacak.
- Ortak olay alanları tanımlanacak: zaman, servis, ortam, sürüm, instance, seviye ve uygun olduğunda
  trace/span, sorgu takip ve iş kimlikleri. Bu kimlikler birbirinin yerine kullanılmayacak;
  eşleştirme mevcut çağrı zincirinden doğrulanacak, eksik ilişki uydurulmayacak.
- Python iç servis kimliği ve müşteri ağındaki Bridge kimliği ayrı güven sınırları olarak korunacak.
  Bridge'in firma/bağlantı ilişkisi sunucuda doğrulanacak; istemcinin bildirdiği firma kimliğine güvenilmeyecek.
- Bridge için sınırlı yerel tampon, çevrimdışı kalma, yeniden bağlanma ve tekrar gönderimde
  yinelenen kayıtların ayıklanması planlanacak. Toplama kesintisi sorgu çalıştırmayı engellememeli.
- Log & Telemetri ekranına Python ve Bridge filtreleri eklenecek. Yetkili yönetici sorgu ayrıntısından
  ilişkili log/trace'e, logdan da ilgili sorguya geçebilecek; kapsam dışında kalan kayıtlar açıkça belirtilecek.
- Müşteri verisi, SQL parametreleri ve sırlar kaynakta maskelenecek; aktarım kimlik doğrulamalı ve şifreli olacak.

Kabul ölçütleri: sentetik bir sorgunun .NET → Python veya .NET → Bridge yolu ilişkili kayıtlarla
izlenebilmeli; yanlış firma/Bridge kimliği reddedilmeli; çevrimdışı tampon sınırı, yeniden gönderim,
maskeleme ve toplama hattı kesintisinde sorgu akışının devamı doğrulanmalı.

Konteyner stdout/stderr loglarının eklenmesi aynı toplama hattı
değerlendirmesinde ayrıca kapsamlandırılacak; mevcut uygulama telemetrisiyle karıştırılmayacak.
