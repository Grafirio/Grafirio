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

## İyileştirme sinyalleri

Özet: tamamlanmış/başarısız istekler, boş sonuçlar, açıklama gereksinimi, olumsuz kullanıcı oyları,
tekrarlanan hatalar, tekrar sorulan sorunlu sorular, aynı bağlamdaki başarı/başarısızlık farkları ve yavaş sorgular.
Başarı oranının paydası yalnızca `completed + failed`; bekleyen, iptal edilen ve açıklama turları dışarıdadır.
Olumsuz oylar yalnızca oy verilmiş kayıtlarla birlikte gösterilir. Yanıt süresinin %95 değeri terminal kayıtlar üzerinden hesaplanır.

Panel içi uyarılar dönem ve eşiklere göre hesaplanır; harici e-posta gönderimi yoktur.
Liste/özet seçilen dönemde en yeni **5.000 kayıtla** sınırlıdır ve sınır dolduğunda panelde belirtilir.
Soru/durum filtreleri bu örneklem üzerinde çalışır. Tam takip veya sorgu numarasıyla arama dönem/örneklem sınırını aşarak
ilgili kaydı doğrudan getirir; eski bir takip numarası `/diagnostics?id=...` ile de açılabilir.

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
