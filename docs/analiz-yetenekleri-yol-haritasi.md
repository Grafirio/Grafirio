# Analiz yetenekleri yol haritası: Power BI açıklarını kapatma

Tarih: 12 Ekim 2026. Bu belge, soru→analiz hattının bugün cevapladıklarını,
Power BI (Q&A + Analytics bölmesi + AI görselleri) karşılaştırmasında tespit
edilen açıkları ve bunları kapatma sırasını kaydeder. Karşılaştırma Microsoft
Learn'deki güncel yetenek listeleriyle yapıldı (Q&A Şubat 2027'de emekliye
ayrılıp Copilot'a devrediliyor; hedef çıta "sohbet eden analiz").

## Bugünkü durum (özet)

Güçlü olduğumuz yerler: Türkçe doğal dil çevirisi, eş anlamlı/sözlük eşleme,
netleştirme ve devam soruları, ölçülmüş ilişkilerle 8 tabloya kadar join,
bire-çok ön toplama, çoklu `having`, pencere hesapları (birikim, hareketli
ortalama, pay, sıra), iki tabloyu karşılaştıran `union`, denetim izi ve
salt-okuma garantileri. Bunların bir kısmı Power BI Q&A'de yok.

Açıklar (Power BI'da olup bizde olmayanlar) aşağıdaki fazlara dağıtıldı.

## Faz 1 — Dönem yuvarlama (ay/çeyrek/yıl) **[temel; diğer fazların ön şartı]**

Bugün `group_by` ham tarih kolonunu gruplar: kolon gün/saat tutuyorsa her
zaman damgası ayrı grup olur ve "aylara göre ciro" fiilen çalışmaz.

- `query_spec.py`: `ColumnRef`'ten türeyen `PeriodBucket` — tek yerden T-SQL
  üretir, mevcut doğrulama kuralları (takma ad kapsamı, benzersiz sıralama)
  aynen geçer:
  - ay: `CONVERT(char(7), kolon, 126)` → `'yyyy-MM'`
  - yıl: `CONVERT(char(4), kolon, 126)`
  - çeyrek: `CONCAT(YEAR(kolon), '-Q', (MONTH(kolon) + 2) / 3)` —
    `DATEPART(QUARTER, ...)` bilerek kullanılmıyor: dönem adı ScriptDom'da
    `IdentifierLiteral` düğümü üretir ve paylaşılan politika onu reddeder
    (`PeriodBucketPolicyTests` bu sözleşmeyi sabitler).
  - gün: `CONVERT(char(10), kolon, 126)`
  Paylaşılan SQL politikası `ConvertCall`, `YEAR`, `MONTH`, `CONCAT`'a
  zaten izin veriyor; **politika değişikliği gerekmez.**
- `agent_analyzer.py`: `aggregation` yolunda zaman kırılımı dönem olarak
  çözülür; NULL tarihler `IS NOT NULL` ile elenir.
- Çeviri prompt'u: zaman kırılımında dönem granülaritesi seçimi
  ("aylara göre" → ay, "yıllara göre" → yıl; varsayılan ay).

## Faz 2 — İleriye dönük tahmin (forecast) **[4. madde — bu oturumun işi]**

`forecaster.py` (Holt-Winters → Holt → lineer trend, güven aralığı) hazır ama
hiçbir ürün akışı çağırmıyor. Power BI'daki çizgi grafik Forecast'ın karşılığı.

- Yeni analiz tipi `forecast`:
  - Girdi: zaman kolonu (Faz 1 dönem kırılımı, v1 aylık), `target_column` +
    `aggregation` ölçüsü, `horizon` 1–36 (varsayılan 6).
  - Seri SQL'de toplanır (seri tavanı 500), `forecast_series(seri, horizon,
    seasonality=12)` çağrılır. En az 4 dönem şart; azsa açık hata.
  - Boşluk ayları `count`/`sum` için 0 ile doldurulur, `avg/min/max` için
    doldurulmaz ve denetim izine not düşülür.
  - Çıktı: tek çizgi grafik — "Gerçekleşen", "Tahmin", alt/üst güven bandı
    (grafik doğrulayıcı `null` kabul ediyor; bant çizilebilir). Insights:
    model adı, ufuk, "tahmin geçmiş desenin devamını varsayar" uyarısı.
  - `union`/`window`/`having` ve çoklu kırılımla birlikte açık hatayla
    reddedilir; sessiz yanlış üretilmez.
- Çeviri prompt'u: "önümüzdeki/gelecek N ay", "yıl sonuna kadar", "ne kadar
  olur" → `forecast`; `regression` "hangi faktörler etkiliyor" sorularına
  daralır. JSON şablonuna `horizon` eklenir.

## Faz 3 — Dönem karşılaştırma ve büyüme yüzdesi

"Bu yıl vs geçen yıl aylara göre", "geçen aya göre % değişim" — karşılaştırmalı
müşteri sorularının en büyük açığı.

- `period_compare: {"unit": "year"}` + `time_axis` (ay ya da çeyrek): eksen
  takvim konumu olur ('01'..'12' / 'Q1'..'Q4'), her yıl ayrı seri olarak
  çizilir (mevcut pivot makinesiyle, union'a gerek kalmadı). Ay dolgusu
  `CONCAT(MONTH(x)/10, MONTH(x)%10)` ile üretilir — `RIGHT`/`LEFT`
  kullanılamaz, ScriptDom bunları özel `RightFunctionCall` düğümü olarak
  ayrıştırır ve politika reddeder (`PeriodBucketPolicyTests` sabitler).
- İki yıl varsa değişim yüzdesi **yalnızca iki yılda da veri olan dönemler**
  üzerinden hesaplanır: devam eden yılın eksik aylarını tam yılla kıyaslamak
  sahte bir düşüş gösterir; kapsam farkı ayrıca not edilir.
- Tek dönem serisinde büyüme otomatiktir (parametresiz): son dönem önceki
  döneme ve aylık seride geçen yılın aynı ayına göre % değişim insight olarak
  döner; içinde bulunulan dönem bitmediyse bu açıkça söylenir. Payda sıfır ya
  da ölçüm yoksa yüzde uydurulmaz, atlanır.
- `period_compare` ek kırılım, `union`, `window` ve `forecast` ile açık
  hatayla reddedilir.

## Faz 4 — Çift ölçü (iki seri tek grafikte)

"Ciro **ve** sipariş sayısı aylara göre" — Power BI'ın "sales vs cost"
karşılığı.

- `QuerySpec.extra_aggregates`: ek ölçüler projeksiyona girer; sıralama,
  `having` varsayılanı ve pencereler İLK ölçüye bağlı kalır. Çıktı adı
  çakışması (ölçü↔ölçü, ölçü↔kırılım) SQL üretilmeden reddedilir.
- Çeviri sözleşmesi `measures: [{column, aggregation, label}]` — yalnızca
  kullanıcı 2+ ölçü istediğinde; en fazla 5 ölçü; `group_by` ya da
  `time_axis` ile çalışır, büyüme yorumu ilk ölçü üzerinden döner.
- Bilerek reddedilen bileşimler: `union`, `period_compare`, `forecast` ve
  "dönem + ek kırılım + çoklu ölçü" (pivot tek değer kolonunu serilere açar;
  ikinci ölçü hangi kolonun pivotlanacağını belirsizleştirir).
- Ölçek notu: seriler tek eksende çizilir; birimleri çok farklı iki ölçü
  için ikinci y-ekseni ileride önyüz işi olarak ele alınabilir.

## Faz 5 — Eksik toplulaştırmalar: distinct count, medyan, yüzdelik

- "Kaç **farklı** müşteri" → `COUNT(DISTINCT kolon)`: `Aggregate`'e
  `count_distinct`; ScriptDom bunu `FunctionCall(COUNT)` olarak gördüğünden
  politika değişikliği beklenmiyor (testle doğrulanacak).
- Medyan/yüzdelik → `PERCENTILE_CONT ... WITHIN GROUP`: düğüm
  (`WithinGroupClause`) politikada izinli ama fonksiyon adı
  `AllowedFunctions`'ta yok. **Paylaşılan `Grafirio.QueryPolicy` değişir** →
  `shared-packages.ref` sürümü, dört servisin yeniden derlenmesi ve Bridge
  exe'nin yeniden yayımlanması gerekir. Bu operasyonel maliyet yüzünden ayrı
  faz; eski Bridge'ler yeni fonksiyonu reddederse hata açık olmalı.

## Faz 6 — Satır listesi cevabı (tablo görseli)

"Son 10 siparişi listele" — Power BI Q&A table/matrix/card döndürür, biz hep
toplulaştırıyoruz.

- Yeni analiz tipi `list`: yalnızca sözlükteki beyaz listeli kolonlar, zorunlu
  `ORDER BY` + `TOP`, sonuç tablo yükü olarak döner.
- `chart_validation.py` + önyüz (useradmin) yeni `table` çıktı tipini tanır.
- Satır verisi döndüğü için kişisel veri riski sözlük beyaz listesiyle sınırlı
  kalır; satır tavanı küçük tutulur (örn. 100).

## Faz 7 — Zaman serisi anomalisi + açıklama

Power BI'ın "Anomalies + explain by" karşılığı. Bugünkü isolation forest satır
bazlı; zaman serisi sapması ve sebep önerisi yok.

- Faz 1 dönem serisi üzerinde artık (residual) bazlı sapma işaretleme
  (forecaster'ın fitted değerlerinden ±kσ).
- "Açıkla": sapan dönemde seçilen kırılımın payını taban dönemlerle
  karşılaştıran ikinci bir gruplama sorgusu; en çok sapan kırılım insight
  olarak sunulur.

## Faz 8 — Key influencers sunumu

Mevcut `regression`/`classification` çıktısı model metrikleri (MAE/R²);
iş kullanıcısı için anlamsız. PyCaret'in feature importance'ı "X'i en çok
etkileyenler" sıralamasına ve sade insight'lara çevrilir. Yeni model eğitimi
gerekmez, sunum katmanı işi.

## Bilinçli kapsam dışı

- **What-if parametreleri** ve **decomposition tree**: etkileşimli görsel/UI
  bileşeni gerektirir; soru-cevap akışına çevrilemez. Devam soruları kısmen
  karşılıyor. İleride ürün kararıyla ele alınmalı.
- Harita görseli: önyüz görsel kütüphanesi kapsamında ayrı iş.

## Sıra ve bağımlılıklar

1 → 2 (forecast dönem serisine muhtaç) → 3 → 4 → 5 → 6 → 7 → 8.
Faz 1+2 tek PR olarak birlikte çıkar (bu oturum). Faz 3–4 prompt + motor işi,
imaj dağıtımı yeterli. Faz 5 paylaşılan paket/Bridge yayını gerektirir.
Faz 6 önyüz değişikliği içerir.

Her fazda: motor birim testleri (bağımlılıksız `RecordingPort` düzeneği),
üretilen SQL'in politika testinden geçtiğinin doğrulanması
(`Grafirio.QueryPolicy` testleri) ve çeviri prompt'u için `evals/` setine
yeni vakalar.
