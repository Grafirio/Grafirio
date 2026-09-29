# Semantik zekâ ölçümü

Grafirio'nun "anlama" tarafı altı yetenekten oluşuyor. Her biri, **doğru cevabı önceden bilinen test
veritabanlarına** karşı ve **kendi katmanında** ölçülüyor. Uçtan uca doğruluk yalnızca "cevap yanlış"
der; katman katman ölçüm yanlışın **nerede** doğduğunu gösterir.

| Yetenek | Sistemdeki karşılığı | Nasıl ölçülür | Ana metrik |
|---|---|---|---|
| Schema Understanding | "Analiz Et" sözlüğü: kolon rolleri, tablo amaçları; profil: hassas kolonlar | Kolon başına altın rol, tablo başına beklenen/karşıt kavramlar, hassas kolon listesi | `role.accuracy`, `sensitive.recall`, `table.opposite_confusions` |
| Relationship Discovery | `RelationshipDiscovery` (FK, ad, değer örtüşmesi) | Altın kenar kümesi + tuzak (kurulmaması gereken) kenarlar | `f1`, `recall.<sınıf>`, `trap_hits`, `precision.high_confidence` |
| Semantic Mapping | Soru → tablo / kolon / filtre (LLM çevirisi) | Beklenen parametrelerin alt kümesi; aynı niyetin 2–3 yazılışı | `accuracy`, `table.accuracy`, `paraphrase.consistency`, `false_abstention.rate` |
| Unknown Data Handling | Netleştirme, kapsam reddi | Cevabı şemada olmayan / belirsiz / hassas sorular | `abstention.recall`, `hallucination.rate` |
| SQL Generation | PyCaret `query_spec` → `audit.executedSql` | Aynı veritabanında çalıştırılan **altın SQL**'in sonucu, grafikteki sayılarla | `execution.accuracy`, `fan_out.suspected` |
| Visualization Accuracy | `chart_type`, grafik verisi | Kabul edilen türler, eksiksizlik, sıra, yapı | `fidelity`, `type.accuracy` |

## Veri setleri (`evals/semantic/`)

- **eticaret** — kolay taban çizgisi. DummyData şemasının sade kopyası; bütün ilişkiler FK olarak
  bildirilmiş. Burada hata varsa sorun zorlukta değil, temelde.
- **lojistik** — zor. FK yok; `L_INT_` gibi önekli adlar, rol önekli kolonlar
  (`ShipperCompanyId`), ithalat/ihracat karşıt tabloları, aynı adlı kolonlar, `ROD/SEA/AIR` kodları,
  koşullu lookup tablosu, yazım hatası (`PostionId`), kısaltma (`ExportRefNo`), Türkçe adlar
  (`ParaBirimi`), öz-referans ve **tesadüfi değer örtüşmesi tuzağı** (`CarrierCompanyNo`).

Her klasör: `schema.sql`, `seed.sql` (belirlenimci: her kurulumda aynı satırlar), `gold.json`,
`questions.json`. Dosyaların başındaki yorumlar alanları açıklıyor.

### Gerçek bir müşteri şeması eklemek

Asıl kanıt budur: sentetik şema kendi varsayımlarımızı doğrular. Yeni bir klasör açın
(`evals/semantic/<ad>/`), şemanın **anonimleştirilmiş** `schema.sql` + `seed.sql`'ini koyun,
`gold.json`'u alanı bilen biri işaretlesin. İşaretlemeyi hızlandırmak için önce aracı çalıştırıp
sistemin kendi tahminini (sözlük: `artifacts/olcum/semantic/<ad>-dictionary.json`) başlangıç olarak
kullanın, yalnızca yanlışları düzeltin. Sonra:

```bash
dotnet run --project tools/Grafirio.SemanticEval -- check-gold --setup --datasets <ad>
```

## Çalıştırma

### Süreç içi (grafirio-semantic) — dört yetenek

DataAnalysis'in gerçek sınıflarını çalıştırır; yığının ayakta olması gerekmez.

```bash
# Test veritabanlarını LocalDB'ye kurar ve ilişki keşfini ölçer (LLM gerekmez):
dotnet run --project tools/Grafirio.SemanticEval -- relationships --setup

# Hepsi (şema rolleri ve eşleme için Azure OpenAI ortam değişkenleri gerekir):
dotnet run --project tools/Grafirio.SemanticEval -- all --publish http://localhost:5080 --label "prompt v3"

# Yalnızca eşlemeyi ölçmek (sözlüğü yeniden üretmeden, LLM maliyeti yarıya iner):
dotnet run --project tools/Grafirio.SemanticEval -- mapping --reuse-dictionary --publish http://localhost:5080
```

LLM ayarları DataAnalysis ile aynı: `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_DEPLOYMENT`,
`AZURE_OPENAI_API_KEY`. Tanımsızsa LLM isteyen ölçümler atlanır ve bu, raporun "Ortam → notes"
alanına yazılır. Araç invariant kültürle çalışır (üretim container'ı gibi): Türkçe Windows'ta
`RegexOptions.IgnoreCase` "I"/"i" eşleşmediği için hassas kolon desenleri farklı sonuç veriyordu.

### Canlı (grafirio-measure semantic-live) — SQL ve görselleştirme

SQL'i PyCaret (Python) ürettiği için bu ikisi kullanıcının yolundan ölçülüyor.

1. Veri setini Grafirio'nun bağlanabildiği SQL Server'a kurun:
   ```bash
   dotnet run --project tools/Grafirio.SemanticEval -- setup --server "localhost,1433" --sql-user sa --datasets eticaret
   ```
   (`SEMANTIC_SQL_PASSWORD` ortam değişkeni parola için.)
2. Grafirio'da bu veritabanına bir bağlantı ekleyin, altın verideki tabloları seçip "Analiz Et"i
   çalıştırın, sorularını yanıtlayın.
3. Ölçün:
   ```bash
   export GRAFIRIO_TOKEN="<Keycloak erişim token'ı>"
   export SEMANTIC_GOLD_DB="Server=localhost,1433;Database=GrafirioEval_eticaret;User Id=sa;Password=...;TrustServerCertificate=True"
   dotnet run --project tools/Grafirio.Measurement -- semantic-live --dataset evals/semantic/eticaret \
     --connection <bağlantı-guid> --publish http://localhost:5080
   ```

## Canlı sinyaller (üretim)

`GET /data-analysis/api/admin/semantic-signals?days=30` — yalnızca `PLATFORM_ADMIN`, yalnızca toplu
rakam. Altın veri olmadan, gerçek kullanımdan her yetenek için dolaylı bir sinyal:

| Yetenek | Sinyal |
|---|---|
| Schema | Analiz başına kurulum sorusu, kullanıcı düzeltmesi (anlam/eş anlamlı/kod), analiz başarısı |
| Relationships | Analiz başına bulunan ve **çıkarsanan** ilişki, önerilen eşleşmelerin kabul oranı |
| Mapping | Tekrar sorma, olumsuz oy, tamamlanma |
| Unknown | Netleştirme oranı, netleştirmenin çözülme oranı |
| SQL | Başarısızlık oranı, sınıfına göre (sql / policy / timeout / bridge) |
| Visualization | Kullanıcının grafik türünü değiştirme oranı (sonuçtan sonra), olumlu oy |

## Admin panel

ProjectAdmin → **Semantik zekâ** (panelin açılış sayfası). Altı kart: ana metrik, önceki koşuya göre
fark, son 12 koşunun trendi, ikincil metrikler ve canlı sinyaller. Bir kart seçilince: metrik
seçilebilen trend grafiği (koşuya tıklanır), o koşunun tüm metrikleri (öncekiyle farkı), canlı
sinyaller, başarısız vakalar (beklenen/gelen ayrıntısıyla) ve koşu tablosu.

Otomatik uyarılar: ilişki keşfinde yutulan SQL hataları, üretimde hiç çıkarsanmayan ilişki,
korunmayan hassas kolon, uydurma cevap.

Değerlendirme koşuları benchmark dashboard'undan okunur (`VITE_BENCHMARK_URL`, varsayılan
`http://localhost:5080`); dashboard'un `Cors:AllowedOrigins` listesi panelin adresini içermeli.
Dashboard'da kimlik doğrulaması yok: yerel ağ dışına açılacaksa önüne erişim kısıtı koyun ve
`Ingest:ApiKey` tanımlayın.

## İlk ölçümün bulguları (2026-09-29)

- **İlişki keşfi, lojistik: 0/18.** `MeasureOverlapAsync` sorgusu SQL Server'da Msg 130 veriyor
  (`SUM(CASE WHEN EXISTS(...))`), hata "aday elendi" diye yutuluyor. Sonuç: FK olmayan hiçbir ilişki
  kurulmuyor — kullanıcının elle tanımladıkları dahil. Birim testleri sahte oturumla yakalamamıştı.
- **Hassas kolon: 7/8.** `TaxNumber`, `tax_?no` desenine uymuyor; değerleri örneklenip modele gidebilir.
- e-ticaret (FK'li): ilişki 10/10, hassas kolon 6/6.
