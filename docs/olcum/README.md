# Ölçüm: sistemi rakamla kanıtlamak

Bu belge Grafirio'nun **zaman, tutarlılık, ölçeklenebilirlik ve harcama** açısından nasıl
ölçüldüğünü ve sonuçların nereye gittiğini anlatır.

Temel kural: **her sonuç tekrarlanabilir, bir commit'e bağlı ve bir öncekiyle
karşılaştırılabilir olmalı.** Tek başına bir rakam bir şey kanıtlamaz; "önce X'ti, şimdi Y"
kanıttır. Bu yüzden her ölçüm bir *koşu* olarak saklanır ve commit bilgisini taşır.

## Dört katman

| Katman | Ne cevaplar | Araç | Nereye gider |
|---|---|---|---|
| 1. Canlı telemetri | Hangi adım yavaş/hatalı, istek başına kaç token | OpenTelemetry (servislerin içinde) | Aspire Dashboard (canlı), `QueryHistories` / `AnalysisConfigs` (kalıcı) |
| 2. Değerlendirme seti | Doğru mu, her seferinde aynı mı, soru başına ne kadar | `grafirio-measure eval` | Benchmark dashboard → Senaryolar |
| 3. Yük testi | Kaç eşzamanlı kullanıcıyı kaldırıyor, nerede kırılıyor | `grafirio-measure load` | Benchmark dashboard → Senaryolar |
| 4. Mikro-benchmark | Sıcak yollar (SQL politikası, kodlayıcılar) ne kadar hızlı | BenchmarkDotNet (`tests/Grafirio.Benchmarks`) | Benchmark dashboard → Koşular |
| 5. Semantik zekâ | Şema, ilişki, eşleme, bilinmeyen veri, SQL, grafik — altın veriye karşı, katman katman | `grafirio-semantic`, `grafirio-measure semantic-live` | Dashboard → Senaryolar, ProjectAdmin → Semantik zekâ |
| + Kullanım raporu | Gerçek kullanım: tamamlanma, bekleme, memnuniyet, maliyet | `grafirio-measure usage` | Benchmark dashboard → Senaryolar |

Grafirio ile benchmark dashboard'u (`benchmarkt` deposu) **birbirine referans vermez**.
Aralarındaki tek bağ `scenario-run/v1` JSON belgesi ve BenchmarkDotNet'in kendi JSON raporu.
Ölçüm aracı her sonucu önce diske yazar (`artifacts/olcum/`); dashboard kapalıysa ölçüm
kaybolmaz, sonradan `publish` ile gönderilir.

---

Semantik zekâ ölçümü ayrı bir belgede: [semantik.md](semantik.md).

## 1. Canlı telemetri

`src/shared/Grafirio.Telemetry` dört servise (gateway, identity, commerce, data-analysis)
ortak OpenTelemetry kurulumu ekler: iz, metrik ve log. **Dışarı aktarım yalnızca
`OTEL_EXPORTER_OTLP_ENDPOINT` tanımlıysa açılır**; tanımsızken servisler eskisi gibi çalışır.

Yerelde: `docker-compose up` Aspire Dashboard'u da başlatır → <http://localhost:18888>.
IDE'den çalışan bir servis için `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`.

### Grafirio'ya özgü ölçümler (`Meter: Grafirio`)

| Ölçüm | Birim | Etiketler | Soru |
|---|---|---|---|
| `grafirio.llm.request.duration` | s | `llm.operation`, `llm.outcome`, `gen_ai.request.model` | LLM çağrısı ne kadar sürüyor (429 beklemeleri dahil) |
| `grafirio.llm.tokens` | token | `llm.operation`, `llm.token.type` (input / cached_input / output / reasoning) | Nereye ne kadar token gidiyor |
| `grafirio.llm.cost` | USD | `llm.operation` | Tahmini maliyet (yalnızca fiyat tanımlıysa) |
| `grafirio.llm.rate_limited` | olay | — | Kota (429) baskısı |
| `grafirio.llm.discarded_responses` | cevap | `llm.operation` | Bütçeye sığmayıp atılan (parası ödenmiş) cevaplar |
| `grafirio.datasource.query.duration` | s | `datasource.route` (direct / bridge), `datasource.method`, `datasource.outcome` | Müşteri veritabanı sorgu süresi |
| `grafirio.datasource.query.rows` | satır | aynı | Sorgu başına satır |
| `grafirio.bridge.database.duration` | s | — | Bridge'in ölçtüğü DB süresi; toplamdan farkı ağ + kuyruk |
| `grafirio.question.duration` / `.outcome` | s / soru | `question.outcome` (submitted / clarification / translation_failed / rejected / error) | Soru çevirisi süresi ve sonucu |
| `grafirio.query.duration` / `.completed` | s / sorgu | `query.status` | Kullanıcının beklediği uçtan uca süre |
| `grafirio.analysis.duration` / `.chunks` | s / parça | `analysis.outcome` | "Analiz Et" süresi ve şema büyüklüğü |
| `grafirio.feedback` | oy | `feedback.rating` (up / down) | Kullanıcı memnuniyeti |

Etiketlerde kullanıcı, şirket, soru metni ya da SQL **yok**: kişisel veri taşırlar ve
seri sayısını patlatırlar.

### Kalıcı kayıt

Toplayıcı kapalı olsa bile harcama kaybolmasın diye değerler satıra da yazılır
(`SchemaPatches` kolonları açılışta ekler):

- `QueryHistories`: `PreparationMs`, `LlmCalls`, `LlmInputTokens`, `LlmCachedInputTokens`,
  `LlmOutputTokens`, `LlmReasoningTokens`, `LlmDurationMs`, `FeedbackRating`,
  `FeedbackComment`, `FeedbackAt`
- `AnalysisConfigs`: `AnalysisDurationMs` ve aynı `Llm*` kolonları

API cevapları (`POST /api/agent/query`, `/status`, `/result`) `usage` ve `preparationMs`
döndürür. Kullanıcı oyu: `POST /api/agent/query/{id}/feedback` `{ "rating": 1 | -1 }`;
tuvaldeki her sonucun altında 👍/👎 olarak görünür.

### LLM fiyatı

Fiyat koda gömülü değil (deployment'a ve sözleşmeye göre değişir). Tanımlamak için
(USD / 1M token):

```
Llm__Pricing__InputPerMillion=...
Llm__Pricing__CachedInputPerMillion=...
Llm__Pricing__OutputPerMillion=...
```

Compose bunları `LLM_PRICE_INPUT_PER_MILLION` / `..._CACHED_INPUT_...` / `..._OUTPUT_...`
ortam değişkenlerinden okur. Ölçüm aracı da aynı değişkenleri kullanır.

---

## 2. Değerlendirme seti (`eval`)

Sabit sorular, beklenen cevaplar, her soru N kez. Ölçülen:

- **accuracy** — beklentiyi karşılayan deneme oranı
- **consistency** — aynı soruya en sık verilen cevabın payı (LLM parametrelerinin kanonik
  parmak izi); `consistency.result` sonuç verisinin tutarlılığı
- **pass.rate** — her denemesi doğru olan vaka oranı (`--pass-threshold` ile gevşetilebilir)
- **latency.p50/p95/p99**, **tokens**, **cost.per_question**, **error.rate**,
  **clarification.rate**

Set dosyası örneği: [`evals/dummydata-eticaret.json`](../../evals/dummydata-eticaret.json)
(DummyData e-ticaret şeması). Beklentiler *alt küme* olarak yazılır — yalnızca doğruluğu
belirleyen alanlar. Metinler büyük/küçük harf ve köşeli parantezden bağımsız karşılaştırılır;
`"re:..."` ile başlayan beklenti düzenli ifadedir. `"status": "clarification"` beklenen
vakalar sistemin **uydurmak yerine sorduğunu** doğrular.

```bash
export GRAFIRIO_TOKEN="<Keycloak erişim token'ı>"
dotnet run --project tools/Grafirio.Measurement -- eval \
  --suite evals/dummydata-eticaret.json --connection <baglanti-guid> \
  --repeat 3 --publish http://localhost:5080 --label "sözlük prompt v2"
```

Çıkış kodu: tüm vakalar başarılıysa 0, değilse 3 — CI'da kalite kapısı olarak kullanılabilir.

## 3. Yük testi (`load`)

Kapalı döngü: N sanal kullanıcı, her biri cevabı alınca bir sonrakini gönderir. Kademeler
(`--stages 1,5,10,25,50`) sırayla koşar; bir kademe SLO'yu (`--slo-p95`, `--max-error-rate`)
aşarsa durulur — o nokta sistemin kırılma noktasıdır.

| Senaryo | İstek | Not |
|---|---|---|
| `health` | `GET /health` | Altyapı tabanı; kimlik gerekmez |
| `history` | `GET data-analysis/api/agent/queries/{connection}` | Kimlikli, veritabanı ağırlıklı okuma |
| `question` | `POST data-analysis/api/agent/query` | **LLM'e para harcatır**; `--allow-llm` şart |
| `custom` | `--path "GET <yol>"` | Serbest |

Kavramlar: **throughput** (SLO içindeki en yüksek istek/sn), **concurrency.max_healthy**,
**scaling.efficiency** (kullanıcı k kat artınca verim kaç kat arttı; 1.0 doğrusal).
Yük üreteci ile sistem aynı makinedeyse verim CPU paylaşımı yüzünden düşük görünür —
kanıt için yükü ayrı bir makineden üretin.

```bash
dotnet run -c Release --project tools/Grafirio.Measurement -- load \
  --scenario history --connection <guid> --stages 1,5,10,25 --duration 30s \
  --slo-p95 1500 --publish http://localhost:5080
```

## 4. Mikro-benchmark

```bash
dotnet run -c Release --project tests/Grafirio.Benchmarks -- --filter "*"
dotnet run --project tools/Grafirio.Measurement -- publish-bdn --publish http://localhost:5080
```

`publish-bdn`, BenchmarkDotNet'in kendi `*-report-full.json` dosyalarını gönderir; dashboard
onları mevcut "Koşular / Trendler / Karşılaştır" sayfalarında gösterir.

## Kullanım raporu (`usage`)

Gerçek kullanımın toplu özeti (gün ya da hafta kırılımında): soru sayısı, aktif kullanıcı
sayısı, tamamlanma/netleştirme/başarısızlık oranı, **tekrar sorma oranı** (aynı kullanıcı
aynı soruyu 24 saat içinde yeniden sormuş — memnuniyetsizliğin dolaylı işareti), olumlu
geri bildirim oranı, bekleme p50/p95, soru ve analiz başına token/maliyet.

Belgeye **yalnızca toplu rakamlar** girer; kullanıcı kimliği, şirket ve soru metni girmez.

```bash
dotnet run --project tools/Grafirio.Measurement -- usage \
  --db "Host=...;Database=grafirio_dataanalysis;Username=...;Password=..." \
  --days 30 --bucket week --publish http://localhost:5080
```

---

## Dashboard

`benchmarkt` deposu: `dotnet run --project src/BenchmarkPlatform.Api` → <http://localhost:5080>.
Senaryolar sekmesi (`/scenarios`) eval, yük ve kullanım koşularını; Koşular sekmesi
mikro-benchmark'ları gösterir. Başka bir makineden gönderim yapılacaksa dashboard'da
`Ingest:ApiKey` tanımlayın ve araca `--publish-key` (ya da `MEASURE_DASHBOARD_KEY`) verin.

## Neyin nereye yayınlanması gerekir

- Telemetri ve veritabanı kolonları: `src/services/**` ve `src/shared/Grafirio.Telemetry/**`
  → container deploy (CI yol filtrelerine eklendi).
- Bridge'in DB süresi (`QueryCompleted.DatabaseMs`): `src/bridge/**` → **masaüstü exe yeniden
  yayınlanmalı**. Alan isteğe bağlı; eski bridge'ler çalışmaya devam eder, yalnızca bu süre
  gelmez.
- 👍/👎 butonu: `src/front/admins/grifirio.useradmin/**` → useradmin imajı.
- Ölçüm aracı, benchmark projesi ve set dosyaları hiçbir imaja girmez.
