using System.Text.Json;

namespace Grafirio.DataAnalysis.Api.Services;

/// <summary>
/// Analiz hattinin LLM tarafi. Iki isi var:
///
///   1. <see cref="BuildSchemaDictionaryAsync"/> — tablo profilinden semantik
///      sozluk uretir. "Analiz Et" adiminda bir kez calisir.
///   2. <see cref="TranslateQuestionAsync"/> — kullanicinin sorusunu o sozlugu
///      kullanarak analiz parametrelerine cevirir. Her soruda calisir.
///
/// Onceki tasarimda uc ayri LLM isi vardi: on analizde semantik sozluk,
/// "Analiz Et"te ham semadan PyCaret config, sorgu aninda da o config'e bakan
/// bir ceviri. Sozluk uretiliyor ama sorgu aninda hic okunmuyordu; model
/// gercek kolon adlarini gormedigi icin olmayan adlar uyduruyordu. Artik tek
/// bir sozluk var ve ceviri onu goruyor.
///
/// Saglayici secimi <see cref="ILlmClient"/> icinde; bu sinif yalnizca prompt
/// kurar ve yaniti ayristirir.
/// </summary>
public class LlmAnalysisService
{
    private readonly ILlmClient? _model;
    private readonly ILogger<LlmAnalysisService> _logger;

    public LlmAnalysisService(ILlmClient llmClient, ILogger<LlmAnalysisService> logger)
    {
        _logger = logger;

        if (!llmClient.IsConfigured)
        {
            _logger.LogWarning("LLM yapılandırılmamış — analiz ve sorgu çalışmayacak.");
            _model = null;
            return;
        }

        _model = llmClient;
    }

    private static LlmResult NotConfigured() => new()
    {
        Success = false,
        IsConfigurationError = true,
        Error = "LLM yapılandırılmamış. Sunucuda AZURE_OPENAI_* değişkenleri tanımlı olmalı."
    };

    /// <summary>
    /// Secili tablolarin profilinden semantik sozluk uretir.
    ///
    /// Bu, sistemin "gidilen ulke" ifadesini <c>ReceiverCompanyCountryName</c>
    /// kolonuna baglamasini saglayan katman. Cikti bilerek yapisal: duzyazi
    /// ozet insan icin, sozluk makine icin. Model emin olamadigi kolonlari
    /// <c>questions</c> altinda bildiriyor; bunlar kullaniciya bir kez sorulup
    /// yanitlari sozluge isleniyor.
    /// </summary>
    public async Task<LlmResult> BuildSchemaDictionaryAsync(string profileJson, CancellationToken ct = default)
    {
        if (_model is null) return NotConfigured();

        _logger.LogInformation("Semantik sözlük isteniyor. Profil uzunluğu: {Len}", profileJson.Length);

        try
        {
            // Varsayilan 2048 tavan bu is icin yetmiyor: onlarca kolonun sozlugu
            // arti sorular tek cevaba sigmali, ustelik reasoning token'lari da
            // ayni butceden dusuyor.
            var text = await _model.GenerateAsync(
                BuildDictionaryPrompt(profileJson), temperature: 0.1, maxTokens: 16000, cancellationToken: ct);

            return new LlmResult
            {
                Success = true,
                Json = ExtractJson(text),
                Explanation = ExtractExplanation(text),
                RawResponse = text
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantik sözlük üretimi başarısız");
            return new LlmResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Kullanicinin dogal dil sorusunu, semantik sozluge bakarak analiz
    /// parametrelerine cevirir.
    /// </summary>
    public async Task<LlmResult> TranslateQuestionAsync(
        string question, string dictionaryJson, string schemaSummary, CancellationToken ct = default)
    {
        if (_model is null) return NotConfigured();

        _logger.LogInformation("Soru çevriliyor: {Question}", question);

        try
        {
            // Bugunun tarihi prompt'a giriyor: "bu yil", "gecen ay", "son 3
            // ay" gibi ifadeler bu olmadan tarih araligina cevrilemez.
            var text = await _model.GenerateAsync(
                BuildTranslationPrompt(question, dictionaryJson, schemaSummary, DateTime.UtcNow),
                temperature: 0.1, maxTokens: 4000, cancellationToken: ct);

            return new LlmResult
            {
                Success = true,
                Json = ExtractJson(text),
                Explanation = ExtractExplanation(text),
                RawResponse = text
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Soru çevirisi başarısız");
            return new LlmResult { Success = false, Error = ex.Message };
        }
    }

    private static string BuildDictionaryPrompt(string profileJson)
    {
        return $$"""
        Sen bir veri modeli uzmanısın. Aşağıda bir müşterinin veritabanından
        çıkarılmış tablo profili var: kolon adları, tipler, istatistikler ve —
        gizlilik politikasının izin verdiği kolonlarda — örnek değerler.

        Görevin bu veritabanının SÖZLÜĞÜNÜ çıkarmak: her TABLONUN ve her
        KOLONUN ne işe yaradığını yaz. Çözemediklerin için veritabanını bilen
        kişiye soru sor.

        Tablolar kolonlar kadar önemli. Kullanıcı "ithalatta en çok hangi
        ülke" diye sorduğunda sistemin önce doğru TABLOYU seçmesi gerekiyor;
        bunu yapabilmesinin tek yolu, senin burada her tablonun ne olduğunu ve
        kullanıcının ondan nasıl bahsedeceğini yazmandır.

        ## Profil
        ```json
        {{profileJson}}
        ```

        ## Sözlük kuralları
        1. YALNIZCA profilde geçen tablo ve kolon adlarını kullan. Ad uydurma.
        2. Adı yanıltıcı olabilir, içeriği olmaz — örnek değerlere bak.
        3. Her kolon için kullanıcının o alandan bahsederken kullanabileceği
           Türkçe karşılıkları yaz.
        4. Her TABLO için de aynısını yap: ne tuttuğunu, bir satırının neyi
           temsil ettiğini ve kullanıcının o tablodan bahsederken
           kullanabileceği Türkçe ifadeleri yaz.
        5. Tablo adlarındaki kalıpları çöz. Önekler genellikle bir aileyi
           (kaynak sistem, modül), gövde ise konuyu anlatır. Aynı öneki
           paylaşan tablolar akrabadır; adları yalnızca bir kelimede ayrışan
           tablolar (Import/Export, In/Out, Order/Offer gibi) birbirinin
           KARŞITIDIR ve karıştırılmaları en pahalı hatadır. Böyle çiftleri
           fark ettiğinde her birinin `synonyms` alanını, kullanıcının hangi
           kelimeyi kullanırsa hangisini kastedeceği ayırt edilecek şekilde
           doldur.
        6. Profildeki `relationships` bilgisini kullan: bir tablonun hangi
           tablolarla bağlantılı olduğunu `relatedTables` alanına yaz.

        ## Soru kuralları — bunlara harfiyen uy

        Soru sormanın TEK sebebi var: bir TABLONUN ya da bir KOLONUN ne
        olduğunu çözememek.

        - Her soru TEK bir tablo ya da TEK bir kolon hakkında olacak ve o
          tablonun/kolonun adını içerecek.
        - Kolon sorusunda `column` alanını doldur, kalıp şu: "<Tablo>
          tablosunda <Kolon> alanını görüyorum, ne işe yaradığını çözemedim.
          Aşağıdakilerden hangisi?"
        - Tablo sorusunda `column` alanını null bırak, kalıp şu: "<Tablo>
          tablosunun ne tuttuğunu çözemedim. Aşağıdakilerden hangisi?"
        - Şıklar o alanın/tablonun OLABİLECEĞİ anlamlar olacak. Kısa, somut,
          en fazla 5 kelime. Sonuncu şık her zaman "Başka bir şey".
        - Soru TEK cümle olacak. Parantez içi açıklama, "yani", "örneğin"
          zincirleri yok. Veritabanını bilen ama teknik olmayan biri okuyup
          hemen cevaplayabilmeli.

        ASLA sorma:
        - Raporlamada neyi görmek istediğini, hangi metriği tercih ettiğini
        - Satırların nasıl sayılacağını, nasıl gruplanacağını, neyin
          toplanacağını
        - Analiz tercihlerini, ülke mi şehir mi bazlı olacağını
        - İki alandan hangisini kullanacağını

        Bunlar sorgu anında verilecek kararlar; burada veritabanını
        öğreniyoruz, rapor tasarlamıyoruz.

        Adından ve içeriğinden anlamı zaten belli olan kolonlara soru sorma
        (CreatedDate, Quantity, CustomerName gibi); aynısı tablolar için de
        geçerli. En fazla 8 soru sor; çözemediğin tablo ya da kolon yoksa
        `questions` boş kalsın.

        Tablo soruları kolon sorularından önce gelsin: yanlış tablo seçmek,
        yanlış kolon seçmekten daha büyük hata.

        JSON bloğunu ```json ve ``` arasında ver:

        ```json
        {
          "sector": "lojistik|perakende|üretim|finans|sağlık|diğer",
          "sectorConfidence": "high|medium|low",
          "tables": [
            {
              "name": "dbo.Shipments",
              "purpose": "Sevkiyat kayıtları — her satır bir gönderi",
              "synonyms": ["sevkiyat", "gönderi", "taşıma", "yük"],
              "relatedTables": ["dbo.Customers"],
              "confidence": "high",
              "isPrimary": true
            }
          ],
          "columns": [
            {
              "table": "dbo.Shipments",
              "column": "ReceiverCompanyCountryName",
              "meaning": "Gönderinin teslim edildiği ülke",
              "synonyms": ["gidilen ülke", "varış ülkesi", "hedef ülke"],
              "role": "dimension",
              "confidence": "high"
            }
          ],
          "questions": [
            {
              "id": "q1",
              "table": "dbo.L_INT_ImportReference",
              "column": null,
              "question": "L_INT_ImportReference tablosunun ne tuttuğunu çözemedim. Aşağıdakilerden hangisi?",
              "options": [
                "İthalat kayıtları",
                "İhracat kayıtları",
                "Stok hareketleri",
                "Gümrük beyannameleri",
                "Başka bir şey"
              ]
            },
            {
              "id": "q2",
              "table": "dbo.Shipments",
              "column": "ReferenceId",
              "question": "Shipments tablosunda ReferenceId alanını görüyorum, ne işe yaradığını çözemedim. Aşağıdakilerden hangisi?",
              "options": [
                "Müşterinin sipariş numarası",
                "Taşıyıcı firmanın takip numarası",
                "Fatura numarası",
                "Sistem içi kayıt numarası",
                "Başka bir şey"
              ]
            }
          ]
        }
        ```

        `role` şunlardan biri: measure (ölçülebilir sayı), dimension (kırılım),
        date (zaman), identifier (kimlik), other.

        JSON'dan sonra ### Açıklama başlığıyla kısa bir özet yaz.
        """;
    }

    /// <summary>
    /// Ceviri prompt'u. Modelin gordugu tek kaynak semantik sozluk: kolon
    /// adlari, ne anlama geldikleri, kullanicinin onlara ne diyebilecegi ve
    /// olcum mu kirilim mi olduklari. Onceki surumde burada ham bir PyCaret
    /// config'i vardi; model kolonun ne oldugunu bilmedigi icin ada bakip
    /// tahmin ediyordu.
    /// </summary>
    private static string BuildTranslationPrompt(
        string question, string dictionaryJson, string schemaSummary, DateTime today)
    {
        return $$"""
        Sen bir veri analizi asistanısın. Kullanıcının sorusunu, aşağıdaki
        sözlüğe bakarak analiz parametrelerine çevir.

        ## Kullanıcı hakkında
        Soruyu yazan kişi teknik değil ve gelişigüzel yazıyor: küçük harfle,
        yazım hatasıyla, eksik kelimeyle, günlük konuşma diliyle. "en çok nereye
        gidiyoruz", "musteri bazinda ciro", "gecen ay kac sevkiyat" gibi. Bu
        normaldir; kullanıcının doğru terimi bulması beklenmiyor, doğru kolonu
        bulmak SENİN işin. Yazım hatalarını ve eksik ekleri tolere et, Türkçe
        karakter kullanılmamış olabilir (ulke = ülke, musteri = müşteri).

        Bir soruyu ancak sözlükte karşılığı GERÇEKTEN yoksa çözemezsin;
        "kullanıcı net yazmamış" bir gerekçe değildir.

        ## Bugünün tarihi
        {{today:yyyy-MM-dd}}

        ## Veritabanı özeti
        {{schemaSummary}}

        ## Semantik sözlük
        Bu veritabanının tek doğru kaynağı. `tables[].purpose` ve
        `tables[].synonyms` her tablonun ne tuttuğunu ve kullanıcının ondan
        nasıl bahsedeceğini, `columns[].synonyms` kullanıcının bir alandan
        bahsederken kullanabileceği ifadeleri, `role` ise kolonun ölçüm mü
        kırılım mı olduğunu söyler. `answers` varsa, veritabanını bilen
        kişinin verdiği yanıtlardır ve sözlükteki tanımı ezer.

        ```json
        {{dictionaryJson}}
        ```

        ## Kullanıcının sorusu
        "{{question}}"

        ## analysis_type nasıl seçilir

        `aggregation` VARSAYILANDIR. "En çok", "en az", "ilk 5", "kaç tane",
        "toplam", "ortalama", "şuna göre dağılım" gibi her soru `aggregation`.
        Bunlar sayma ve gruplama sorularıdır; makine öğrenmesi gerektirmezler.

        Diğerlerini yalnızca kullanıcı açıkça isterse seç:
        - `statistics`  : "özet istatistik ver", "dağılımı betimle"
        - `correlation` : "hangi alanlar birbiriyle ilişkili"
        - `regression`  : "tahmin et", "öngör" (sayısal hedef)
        - `classification` : "sınıflandır", "hangi gruba girer"
        - `anomaly`     : "aykırı", "anormal", "sıra dışı"
        - `clustering`  : "segmentlere ayır", "kümele"

        Kullanıcı bunlardan birini istemediyse `aggregation` dışında bir şey
        seçme. Sayma sorusuna `statistics` demek, soruyla ilgisiz kolon
        ortalamaları döndürür.

        ## Kurallar
        1. YALNIZCA sözlükte geçen tablo ve kolon adlarını kullan. Kolon adı
           uydurma, tahmin etme, benzetme yapma.
        2. ÖNCE TABLOYU SEÇ, SONRA KOLONU. Sorunun konusunu `tables[].purpose`
           ve `tables[].synonyms` ile eşleştirip `target_table`'ı belirle;
           kolonları ancak ondan sonra seç. Aynı kolon adı birden fazla
           tabloda bulunabilir, o yüzden kolondan tabloya gitmek yanlış
           tabloya götürür.
        3. Sorunun konusuyla ÇELİŞEN tabloyu seçme. Kullanıcı "ithalat"
           diyorsa ihracat tablosu, "gelen" diyorsa giden tablosu yanlıştır —
           kolonları ne kadar uysa da. Böyle bir çelişki görüyorsan ve doğru
           tablonun hangisi olduğundan emin değilsen, tahmin etmek yerine
           Kural 9'u uygula.
        4. Kullanıcının ifadesini `synonyms` üzerinden eşleştir. Örneğin
           "gidilen ülke" sözlükte hangi kolonun eş anlamlısıysa o kolondur.
        5. `role` alanına uy: toplanacak/ortalanacak alan `measure`, gruplama
           yapılacak alan `dimension`, zaman filtresi `date` olmalı.
        6. `aggregation` seçtiysen `group_by` MUTLAKA dolu olmalı ve kırılım
           yapılacak `dimension` kolonunu içermeli. Satır sayısı soruluyorsa
           `aggregation: "count"`, `target_column: null` yeterlidir.
        7. `role` değeri `identifier` olan kolonları ölçüm olarak kullanma.
           Kimlik numarasının ortalaması anlamsızdır; onları yalnızca saymak
           (`count`) için kullan.
        8. "İlk 5", "en çok 10" gibi ifadeleri `limit` alanına yaz.
        9. Soruyu karşılayan TABLOYU ya da KOLONU sözlükte bulamıyorsan
           uydurma — `target_table` alanını boş bırak ve `description` içinde
           kullanıcıya SORULACAK cümleyi yaz. Bu cümle doğrudan kullanıcıya
           gösterilecek: neyi çözemediğini söyle ve hangisini kastettiğini sor.
           Kolon için örnek: "Hangi tutardan bahsettiğinizi çözemedim — navlun
           bedeli mi, sigorta bedeli mi?" Tablo için örnek: "İthalat mı ihracat
           mı sorduğunuzu çözemedim — hangisini istersiniz?" Teknik terim,
           tablo adı ve kolon adı kullanma.
        10. Sonucu en iyi gösteren `chart_type`'ı seç, `chart_title`'ı Türkçe
            yaz.

        ## Zaman ifadeleri

        `filters` üç biçim kabul eder:

        - Eşitlik   : `"Ulke": "Almanya"`
        - Liste (IN): `"Ulke": ["Almanya", "Hollanda"]`
        - Aralık    : `"SevkTarihi": { "gte": "2026-01-01", "lt": "2027-01-01" }`

        "Bu yıl", "geçen ay", "son 3 ay", "2025'te" gibi ifadeleri yukarıdaki
        tarihe göre hesaplayıp ARALIK biçiminde yaz ve `role: date` olan kolonu
        kullan. Zaman ifadesini görmezden gelme: kullanıcı "bu yıl" diye sorup
        tüm zamanların sonucunu görürse bunu anlamasının hiçbir yolu yok.

        ## Örnek
        Soru: "En çok gidilen 5 ülkeyi bana sütun grafiği yap"
        Sözlükte `ReceiverCompanyCountryName` kolonu "gidilen ülke" eş
        anlamlısıyla ve `role: dimension` ile geçiyorsa:
        `analysis_type: "aggregation"`, `group_by: ["ReceiverCompanyCountryName"]`,
        `aggregation: "count"`, `target_column: null`, `limit: 5`,
        `sort_order: "desc"`, `chart_type: "bar"`.

        JSON bloğunu ```json ve ``` arasında ver:

        ```json
        {
          "analysis_type": "aggregation|statistics|correlation|regression|classification|anomaly|clustering",
          "target_table": "dbo.Shipments",
          "target_column": "kolon_adı veya null",
          "feature_columns": ["kolon1", "kolon2"],
          "filters": { "kolon_adı": "değer | [değer, ...] | { \"gte\": \"...\", \"lt\": \"...\" }" },
          "aggregation": "sum|avg|count|min|max|none",
          "group_by": ["kolon_adı"],
          "sort_by": "kolon_adı",
          "sort_order": "desc",
          "limit": 10,
          "chart_type": "bar|line|pie|doughnut|scatter|heatmap",
          "chart_title": "Grafik Başlığı",
          "description": "Bu analizin ne yapacağının kısa açıklaması"
        }
        ```

        JSON'dan sonra ### Açıklama başlığıyla, hangi kolonu neden seçtiğini
        tek cümleyle yaz.
        """;
    }

    private static string ExtractJson(string text)
    {
        // ```json ... ``` bloğunu bul
        var startMarkers = new[] { "```json", "```JSON" };
        var startIdx = -1;

        foreach (var marker in startMarkers)
        {
            startIdx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIdx >= 0)
            {
                startIdx += marker.Length;
                break;
            }
        }

        if (startIdx < 0)
        {
            // Direkt JSON dene
            var firstBrace = text.IndexOf('{');
            var lastBrace = text.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                return text[firstBrace..(lastBrace + 1)];

            return "{}";
        }

        var endIdx = text.IndexOf("```", startIdx, StringComparison.Ordinal);
        if (endIdx < 0) endIdx = text.Length;

        return text[startIdx..endIdx].Trim();
    }

    private static string ExtractExplanation(string text)
    {
        var idx = text.IndexOf("### Açıklama", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // Baslik farkli yazilmis olabilir: son kod blogundan sonraki ilk
            // basligi al.
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) idx = text.IndexOf("###", lastFence, StringComparison.Ordinal);
        }

        return idx < 0 ? "" : text[idx..].Trim();
    }
}

/// <summary>
/// LLM cagrilarinin ortak sonucu. Onceden her metodun kendi sonuc tipi vardi
/// (<c>GeminiAnalysisResult</c>, <c>GeminiQueryResult</c>, <c>GeminiChatResult</c>)
/// ve alan adlari isle ortusmuyordu — semantik sozluk <c>PyCaretParamsJson</c>
/// alaninda tasiniyordu.
/// </summary>
public class LlmResult
{
    public bool Success { get; set; }

    /// <summary>Modelin dondurdugu JSON govdesi.</summary>
    public string Json { get; set; } = "{}";

    /// <summary>JSON'dan sonra gelen duzyazi ozet.</summary>
    public string Explanation { get; set; } = "";

    public string RawResponse { get; set; } = "";

    public string? Error { get; set; }

    /// <summary>
    /// Hata sunucu yapilandirmasindan mi kaynaklaniyor (LLM tanimli degil) —
    /// yoksa cagrinin kendisi mi basarisiz oldu. Uc bunu ayirt edip 503 mu
    /// yoksa 502 mi donecegine karar veriyor: ilki "sunucu eksik
    /// yapilandirilmis", ikincisi "beklenmedik hata" demek ve mudahalesi farkli.
    /// </summary>
    public bool IsConfigurationError { get; set; }
}
