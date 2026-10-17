# Sorgu izleme ve doğrulama

Manager panelinde **Sorgu izleme** (`/diagnostics`) bölümü bulunur. API, gateway üzerinden
`/data-analysis/api/admin/diagnostics` adresindedir. Tüm okuma ve yönetim uçları sunucuda
`PLATFORM_ADMIN` rolünü kontrol eder. Firma kullanıcıları başka firmaların kayıtlarını okuyamaz.

## Bir hatayı incelemek

1. Kullanıcının hata mesajındaki **Takip** numarasını arayın veya dönem/firma/bağlantı filtrelerini kullanın.
2. Kaydı açın. Soru, hata aşaması, model ve uygulama sürümü, parametreler ve hesaplama izini inceleyin.
3. Aynı sorunun başarılı örneğini de seçip **Karşılaştır** bölümünü açın. Farklı alanlar işaretlenir.
4. İncelemeyi `İnceleniyor` durumuna alın; bulguyu ve çözümü not edin.
5. Doğru parametreleri belirledikten sonra **Doğrulama örneği oluştur** bölümünden beklenen alanları kaydedin.
6. Düzeltmeden sonra örneği tekrar kontrol edin ve inceleme durumunu güncelleyin.

## Kayıtların kapsamı

- Teşhis kayıtları `QueryDiagnostics` tablosunda, SQL çalıştırma yetkisi taşıyan `QueryHistories` kayıtlarından ayrıdır.
- Şirketine ait bir bağlantıya gönderilen isteğin kaydı, sözlük/çeviri/doğrulama işlemleri başlamadan oluşturulur.
- Çeviri başlamadan bağlam; doğrulamadan önce model parametreleri kalıcılaştırılır. Normal dönüş, hata ve iptalde son durum kaydedilir.
- `X-Diagnostic-Id` yanıt başlığı isteğin takip numarasıdır. Gateway ve API bunu tarayıcıya açar.
- Model yanıtı en fazla 64.000 karakter tutulur; kesilmişse kayıtta belirtilir.
- Sözlük ve bağlam SHA-256 ile işaretlenir. Bağlam karşılaştırması soru, önceki konuşma, sözlük, tablo seçimi,
  model, prompt sürümü, uygulama sürümü ve referans gününü dikkate alır. Aynı metin tek başına tutarsızlık sayılmaz.
- İşin sonraki durumu ve hesaplama izi bağlı `QueryHistory` kaydından okunur; sorgu listesindeki grafik gövdeleri yüklenmez.
- Eski sorgular incelenebilir; kaydedilmemiş tarihsel bağlam/sürümler tahmin edilmez.
- Kimlik doğrulama/rate-limit/model binding katmanında reddedilen istekler ve PyCaret yapılandırma hataları servis logu/telemetri kapsamındadır;
  doğrulanmamış bağlantılar için müşteri ayrıntısı içeren teşhis kaydı oluşturulmaz.
- Servis aniden durursa 15 dakikadan eski, sorgu işine bağlanmamış hazırlık kayıtları panelde kesintiye uğramış olarak gösterilir.

Model deployment adı kaydedilir. Uygulama sürümü önce `GRAFIRIO_BUILD_SHA`, yoksa assembly informational version'dan alınır.
Dağıtımda `GRAFIRIO_BUILD_SHA` değişkenine commit kimliğini verin. Prompt değiştiğinde `QueryDiagnostics.PromptVersion` değerini güncelleyin.

### Açık ad/kod ifadelerinde çeviri kontrolü

`X adlı ürün`, `X isimli müşteri`, `ürün adı X` ve açık SKU/kod ifadeleri, sözlükteki ilgili
ad/kod alanlarıyla karşılaştırılır. Sayı içeren bir adın sayısı SKU veya kimlik olarak yorumlanamaz.
Basit `X adlı ...` ifadelerinde adın tümü korunur; örneğin `Şarjlı tablet 7863` yalnızca `7863` veya
`Şarjlı tablet` hâline getirilemez. Müşteri, ürün, tedarikçi, depo, kategori ve marka için
sözlük anlamları, eş anlamlıları ve sorgunun gerçek tablo/alias kapsamı dikkate alınır.

Çelişki varsa modelin çevirisi en fazla **bir kez** yeniden istenir. Çelişki devam ederse
`entity_filter_mismatch` ile durulur; SQL işi oluşturulmaz. Doğru ilk çeviride ikinci model çağrısı yoktur.
İki denemenin maskelenmiş yanıtları ve son doğrulama notu teşhis kaydında görülebilir.
Bu kontrol açık Türkçe ad/kod kalıplarına yöneliktir; belirsiz ifadeler için genel varlık araması
ve veride eşleşme doğrulamasının yerini tutmaz. Tahmin süresi sınırlarını değiştirmez.

## İyileştirme sinyalleri

Özet: tamamlanmış/başarısız istekler, boş sonuçlar, açıklama gereksinimi, olumsuz kullanıcı oyları,
tekrarlanan hatalar, tekrar sorulan sorunlu sorular, aynı bağlamdaki başarı/başarısızlık farkları ve yavaş sorgular.
Başarı oranının paydası yalnızca `completed + failed`; bekleyen, iptal edilen ve açıklama turları dışarıdadır.
Olumsuz oylar yalnızca oy verilmiş kayıtlarla birlikte gösterilir. Yanıt süresinin %95 değeri terminal kayıtlar üzerinden hesaplanır.

Panel içi uyarılar dönem ve eşiklere göre hesaplanır; harici e-posta gönderimi yoktur.
Liste ve özet aynı dönem/firma/bağlantı/durum/hata/arama filtrelerini kullanır.
Sayfa numarası yalnızca listeyi etkiler; toplam ve özet bütün eşleşmeleri kapsar.
5.000 kayıt örneklem sınırı kaldırılmıştır. `sampled` false döner;
`sampleLimit: 5000` eski istemciler için korunan tarihsel sözleşme alanıdır, uygulanmış bir sınır değildir.
Tam takip veya sorgu UUID'siyle arama, hem listede hem özette dönem sınırını aşar;
diğer filtreler uygulanmaya devam eder. `/diagnostics?id=...` ayrıntı bağlantısı korunur.

### Metrik tanımları (A aşaması)

- `total` teşhis isteği sayısıdır; çalıştırılan SQL sayısı veya doğru cevap sayısı değildir.
- Ana gruplar birbirini dışlar: `completed`, `failed`, `pending`, `clarification`, `cancelled`, `other`.
- `pending`: preparing, submitted, queued, processing, cancelling. Tanınmayan durumlar `other` içinde korunur.
- `total = completed + failed + pending + clarification + cancelled + other`.
- `emptyResults`, yalnızca failed durumunda empty_result kodu taşıyan kayıtlardır; failed sayısına yeniden eklenmez.
- `successRate = completed / (completed + failed)`; payda sıfırsa null. Bu işlem başarı oranıdır, sonuç doğruluğu değildir.
- Mevcut `terminalCount` ve nearest-rank `p95Ms` kapsamı korunur: completed, failed, clarification, cancelled.
- Hata kodu önceliği: sonuçtaki errorCode, audit.errorCode, saklanan teşhis kodu, hata metninden sınıflandırma. Boş kodlar atlanır.
- B aşamasıyla liste ve özet aynı filtreleme kurallarını kullanır; API yolları ve mevcut alanlar korunur.

### B aşaması — ortak filtreleme ve tam sayım

- Firma, bağlantı, tarih ve etkin durum filtreleri veritabanında uygulanır.
  Bağlı QueryHistory durumu önceliklidir; 15 dakikadan eski hazırlık kaydı için mevcut kesinti kuralı korunur.
- Legacy kayıtlar NOT EXISTS ile bağlı teşhislerden ayrılır; bir sorgu iki kere sayılmaz.
- Arama/hata filtresi yokken COUNT ve durum GROUP BY veritabanında çalışır.
  Liste sayfası için her kaynaktan en fazla sayfa numarası × 50 aday alınır, kararlı tarih/kimlik sırasıyla birleştirilir.
- Maskelenmiş metin araması ve tarihsel JSON/hata metninden kod türetme uygulama tarafında ortak kurallarla çalışır.
  Bu filtrelerde eşleşen kapsamın hafif kayıtları taranır; kesinti veya örneklem uygulanmaz.
- Bağlam, sözlük, parametre ve olay gövdeleri liste/özet sorgularında yüklenmez;
  başarısız QueryHistory kayıtlarının ResultJson alanı hata kodu çözümü için okunur.
  P95 ve inceleme önerileri uygulama tarafında hesaplanır. Çok büyük filtre kapsamlarında bellek/maliyet sınırı devam eder.
- Şema/indeks değişikliği yapılmadı. PostgreSQL SQL çevirisi bağlantı açmadan test edilir;
  üretimde sorgu planı veya performans ölçümü yapılmamıştır.
- C aşamasında panel, aşağıdaki birleşik okuma yoluna geçirilmiştir. Eski ayrı uçlar korunur.

### C aşaması — tutarlı veri görüntüsü

Panel `GET /api/admin/diagnostics?includeSummary=true` ile listeyi ve özeti tek yanıt olarak alır.
Eski liste alanları korunur; yalnızca bu seçenek istendiğinde `summary` ve UTC `capturedAt` eklenir.
Sayfalama yalnızca items alanına uygulanır. Özet bütün filtrelenmiş, belleğe alınmış kayıt kümesinden üretilir;
bu nedenle sayfa boş olsa bile toplam ve özet korunur. Eski `/summary` ve varsayılan liste sözleşmesi devam eder.

PostgreSQL'de birleşik okuma Repeatable Read işlemi içinde teşhis, legacy geçmiş ve ayarları okur.
İşlem Npgsql'nin yapılandırılmış yeniden deneme stratejisi içinde çalışır; başarısız denemenin kısmi yanıtı yayımlanmaz.
Ortak capturedAt; dönem sınırları, eski hazırlık sınıflandırması ve devam eden istek sürelerinde kullanılır.
Eski `/summary` ucunun birden fazla okuması da aynı izolasyonla korunur.
Ayrı HTTP istekleri veya farklı sayfa yenilemeleri arasında aynı snapshot tutulmaz.
`capturedAt` veritabanında saklanan bir snapshot kimliği değildir; bu yanıtın UTC hesaplama referansıdır.

Birleşik yol özeti zaten yüklenen kayıtlardan hesaplar, ek GROUP BY okuması yapmaz.
Firma/bağlantı/tarih/durum filtreleri SQL'de kalır; büyük kapsamların bellek maliyeti B aşamasında belirtilen sınırdır.
İşlem yalnızca mevcut veriyi okur; şema, kayıt veya saklama ayarı değiştirmez.

Frontend bir yanıtın liste ve özetini birlikte uygular. Filtre değişimi/ayrılma eski isteği hemen geçersiz kılar;
300 ms debounce sırasında gelen eski yanıtlar ve hatalar güncel ekranı değiştiremez.

Testler bellek içi veritabanı ve taklit bağımlılıklarla çalışır. PostgreSQL eşzamanlılık testi veya üretim bağlantısı
çalıştırılmamıştır; ilişkisel snapshot garantisi Repeatable Read işlemiyle sağlanır. Frontend yarış testleri:
`node --test tests/diagnosticLoader.test.js` (ProjectAdmin dizininde).

### D aşaması — görünürlük ve aksiyon

Mevcut kart/tablo stilleri korunur; özet yanıtına eklemeli `visibility` ve `comparison` alanları eklenir.
`visibility` ham durum dağılımı, sıklığa göre hata listesi, firma ve firma/bağlantı kırılımları,
süre örneklemi ve gerekçeli inceleme adaylarını içerir. Firma, bağlantı, durum ve hata satırları
aynı filtre kapsamındaki sorgu listesine geçiş yapar; adaylar sorgu ayrıntısını açar.
Kimlikler kayıttaki gerçek değerlerdir; firma adı veya sorgu yürütme kanıtı uydurulmaz.
`executionJobsCreated` yalnızca QueryId bulunan istek sayısıdır, SQL çalıştırma sayısı değildir.

Yeni `visibility.latency` ve kırılım/dönem süreleri aynı örneklem üzerinde hesaplanır:
terminal durumda, CompletedAt bulunan ve negatif olmayan süreye sahip kayıtlar.
Ortalama aritmetik ortalama, P95 nearest-rank yöntemidir. Eksik süreler sıfır kabul edilmez;
sampleCount/missingCount ayrıca verilir. Mevcut üst düzey p95Ms alanı eski sözleşmesiyle korunur;
panel yeni süre örneklemini gösterir. Boş örneklemde ortalama/P95 null döner.

İnceleme adayları başarısızlık, olumsuz oy, açıklama gereksinimi veya yavaşlık gerekçesi taşır.
Düzeltildi/doğrulandı kayıtlar dışlanır; legacy kayıtlarda inceleme durumu bilinmez.
Bir aday bir kez gösterilir, birden fazla gerekçesi olabilir. Başarısızlar, gerekçe sayısı ve süreye göre
sıralanır; en fazla 20 adayla birlikte tam aday sayısı verilir. Mevcut tekrar/tutarsızlık incelemeleri korunur.

Dönemler UTC oluşturulma zamanına göre [şimdi-N gün, şimdi) ve [şimdi-2N gün, şimdi-N gün) aralıklarıdır.
Her iki aralığa aynı filtreler uygulanır, 365 günlük seçimde önceki aralık da tam 365 gündür.
İki dönem aynı Repeatable Read işlemi içinde okunur. Geçmiş dönemin o günkü durumunu yeniden kurmaz;
isteklerin şu an bilinen son durumlarını karşılaştırır. Başarı oranı değişimi yüzde puan,
süre farkları milisaniye olarak verilir; eksik payda/örneklemde fark null olur.
Takip UUID'si araması dönem dışına çıkabildiğinden karşılaştırma kapalıdır.
Legacy kayıtlar ve saklama süresini aşan aralıklar için kapsam uyarısı gösterilir;
boş bir dönem, geçmişte hiç trafik olmadığının kanıtı sayılmaz.

Bu aşama şema, migration, geçmiş kayıt düzeltmesi veya benchmark çalıştırması içermez.

## Doğrulama örnekleri

`DiagnosticTestCases` bağımsız bağlam kopyalarını saklar. Kontrol, kaydedilen referans tarih ve maskelenmiş konuşmayla
**güncel** model/prompt çevirisini çalıştırır; parametre şekli ve tablo kapsamını doğrular.
Beklenen JSON yalnızca tanımlanan alanları karşılaştırır; örneğin açıklama metninin değişmesi teste etki etmez.
SQL çalıştırılmaz. Bu, anlamsal eşleme testi olup veri, sorgu sonucu veya tahmin modelinin doğruluğunun kanıtı değildir.
Örnekler model kullanımı oluşturur. Son 20 kontrol sonucu saklanır. Beklentiler panelden düzenlenebilir ve örnekler silinebilir;
eski kontrolün beklentisi değişmişse panel yeniden kontrol gerektiğini belirtir. Kaydedilen beklentiyi insanın kontrol etmesi gerekir;
başarısız model çıktısını otomatik olarak doğru beklenti kabul etmeyin.

Manager panelinden tek tek veya tüm örnekler sırayla çalıştırılabilir. Dağıtım öncesi kontrol için:

```powershell
# Değerleri CI secret/ortam ayarlarından sağlayın; token'ı rapora yazmayın.
node tools/run-diagnostic-tests.mjs
```

Gerekli ortam değişkenleri: `GRAFIRIO_GATEWAY_URL`, `GRAFIRIO_MANAGER_TOKEN`.
İsteğe bağlı: `GRAFIRIO_DIAGNOSTIC_REPORT` (çıktı dosyası).
Betik boş test setinde veya başarısız kontrolde sıfırdan farklı çıkış kodu verir.
Raporda müşteri verisi, token veya ham model cevabı yer almaz.

## Saklama ve güvenlik

Varsayılan teşhis saklama süresi 90 gün; panelden 7–365 gün ayarlanabilir.
Altı saatte bir eski teşhis kayıtları ve yönetim işlem kayıtları temizlenir.
Kaydedilmiş test örnekleri ve mevcut kullanıcı sorgu geçmişi bu işlemle silinmez.
Tekrar eşiği varsayılan 3, yavaşlık eşiği 60 saniyedir.

E-posta, 11 haneli numaralar ve bilinen credential anahtarları maskelenir. Bu tüm kişisel veri türlerini tespit eden bir DLP sistemi değildir;
soru/SQL/sözlük hâlâ firma verisi içerebilir ve yalnızca platform yöneticilerine açıktır.
İnceleme, ayar değişikliği, test oluşturma/düzenleme/silme ve kontrol işlemleri `DiagnosticAdminEvents` tablosunda aktör ve tarihle saklanır.

Yeni tablolar mevcut `EnsureCreated + SchemaPatches` yaklaşımına uygun olarak başlangıçta idempotent oluşturulur (`DiagnosticSchema`).
Dağıtım hesabının DDL yetkisi olmalıdır. PostgreSQL üzerinde canlı şema kontrolü ayrıca yapılmalıdır.

## Yerel doğrulama

```powershell
dotnet test tests/Grafirio.DataAnalysis.Tests
cd src/front/admins/grifirio.projectadmin
npm run build
npx eslint src/pages/DiagnosticsPage.jsx src/services/diagnosticApi.js src/App.jsx
```

Yeni servis testleri erken hata kayıtlarını, SQL yetkisi ayrımını, şirket dışı istekleri, tüm admin uçlarının erişim kontrolünü,
maskelenmeyi, eski kayıtları, bağlam karşılaştırmasını ve eski tarihle yapılan doğrulamayı kapsar.
