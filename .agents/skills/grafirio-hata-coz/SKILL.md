---
name: grafirio-hata-coz
description: Grafirio uygulama hatalarını takip numarası, çağrı zinciri ve ilgili testlerle teşhis et ve istenen kapsamda düzelt. Üretim erişimi veya dağıtım yetkisi sağlamaz.
---

# Grafirio hatasını çöz

Depo kökündeki `AGENTS.md` ve `docs/gelistirme-rehberi.md` test haritasını kullan. Mevcut değişiklikleri önce incele; hata düzeltmesini başka devam eden işlerden ayırt et.

Hatanın tetikleyicisini, beklenen/gerçek davranışını ve kapsamını belirle. Takip numarası varsa `docs/query-diagnostics.md` üzerinden X-Diagnostic-Id, QueryDiagnostics ve bağlı QueryHistory ilişkisini izle. Uzak kayıt erişimi yoksa kod ve yerel yeniden üretmeyle ilerle; görmediğin kayıtlar hakkında sonuç uydurma.

UI, gateway, .NET, Python ve Bridge sınırlarında ilk bozulan sözleşmeyi bul. HTTP başarısını doğru analiz sonucuyla eşitleme. Semantik hatalarda soru çevirisi, filtre/tablo seçimi, SQL politikası, hesaplama ve sunumu ayrı incele. Timeout'u büyütmeden veya yeniden deneme eklemeden önce hatanın hangi katmandan geldiğini doğrula.

Yetki/bağlantı hatalarında firma sahipliği, platform admin rolü, Bridge kimliği ve iç servis kimliğini ayrı kontrol et. Mevcut güvenlik kontrolünü kaldırarak sorunu gizleme. Bir bağlantı başarısızsa otomatik olarak başka müşteri bağlantısını deneme.

Uygun olduğunda küçük bir yeniden üretme veya mevcut testte gözlemlenebilir davranış kontrolü kullan. Yetkili kapsamda kök nedeni düzelt ve etkilenen testleri çalıştır. `result-validation.md` içindeki özel SQL ortam koşullarını ve uzak/model çağrılarının yan etkilerini yerel testlerden ayır.

Sonuçta neden, değişen davranış, doğrulama ve kalan belirsizliği kısa anlat. Aynı hatanın tekrarını yakalayacak mevcut mekanizmayı belirt; ancak gerekli olmayan genel bir refaktör veya yeni altyapı ekleme.
