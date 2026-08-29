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
    /// <param name="chunkProfiles">
    /// Her biri ayri bir cagriya girecek alt profiller (bkz.
    /// <c>DictionaryChunks.Split</c>). Tek elemanliysa davranis eskisiyle
    /// birebir ayni.
    /// </param>
    /// <param name="allTableNames">
    /// Secilen BUTUN tablolarin adlari. Her cagriya baglam olarak giriyor:
    /// sozluk kurallarindan biri (Import/Export gibi karsit ciftleri
    /// isaretlemek) tablo adlarini bir arada gormeyi gerektiriyor ve
    /// parcalanmis bir cagri kendi disindaki tablolari goremezdi. Yalnizca
    /// adlar gidiyor, profil degil — maliyeti ihmal edilebilir.
    /// </param>
    /// <param name="onProgress">
    /// Her dalga bittiginde tamamlanan parca sayisiyla cagriliyor. Yirmi
    /// parcalik bir semada kullanicinin on bes dakika bos bir spinner
    /// izlemesi, zaman asiminin kendisinden daha kotu.
    /// </param>
    public async Task<LlmResult> BuildSchemaDictionaryAsync(
        IReadOnlyList<string> chunkProfiles,
        IReadOnlyList<string> allTableNames,
        Func<int, int, Task>? onProgress = null,
        CancellationToken ct = default)
    {
        if (_model is null) return NotConfigured();
        if (chunkProfiles.Count == 0)
            return new LlmResult { Success = false, Error = "Profil boş; sözlük üretilemez." };

        _logger.LogInformation(
            "Semantik sözlük isteniyor. {Chunks} parça, {Tables} tablo.",
            chunkProfiles.Count, allTableNames.Count);

        // Sonuclar dizide, indeksleriyle duruyor: paralel calisan cagrilarin
        // bitis sirasi degisken ama sozlugun icerigi degismemeli. Ayni secim
        // her calistirmada ayni sozlugu uretsin.
        var parts = new string[chunkProfiles.Count];
        var explanations = new string?[chunkProfiles.Count];
        var completed = 0;

        try
        {
            // Parcalar DALGALAR halinde calisiyor. Ilk surumde sirayla
            // cagriliyorlardi — yedi parcada dogru takasti, ama sahadaki sema
            // yirmi parca cikardi ve yirmi ardisik cagri analizi kullanilamaz
            // hale getiriyor. Sinirsiz paralellik de dogru degil: Azure'un 429
            // kotasi dakika basina isliyor.
            //
            // Dalga sinirinin ilerlemeyi ana akista bildirmek gibi bir yan
            // faydasi da var: DbContext'e paralel is parcaciklarindan
            // dokunulmuyor.
            for (var start = 0; start < chunkProfiles.Count; start += MaxParallelChunks)
            {
                var end = Math.Min(start + MaxParallelChunks, chunkProfiles.Count);
                var wave = new List<Task>(end - start);

                for (var i = start; i < end; i++) wave.Add(RunChunk(i));

                await Task.WhenAll(wave);

                completed = end;
                if (onProgress is not null) await onProgress(completed, chunkProfiles.Count);

                if (chunkProfiles.Count > 1)
                {
                    _logger.LogInformation(
                        "Sözlük parçaları tamamlandı: {Done}/{Total}",
                        completed, chunkProfiles.Count);
                }
            }

            return new LlmResult
            {
                Success = true,
                Json = SchemaDictionaryMerge.Combine(parts),
                Explanation = string.Join("\n\n",
                    explanations.Where(e => !string.IsNullOrWhiteSpace(e))),
                RawResponse = string.Join("\n\n", parts)
            };
        }
        catch (Exception ex)
        {
            // Bir parca dusunce analizin tamami dusuyor. Kalanla devam etmek,
            // o tablolarin sozlukte hic olmadigi bir analizi "hazir"
            // isaretlemek olurdu; kullanici onlari sorunca "cozemedim" cevabi
            // alir ve sebebini gorecegi hicbir yer olmaz.
            _logger.LogError(ex,
                "Semantik sözlük üretimi başarısız ({Done}/{Total} parça tamamlanmıştı)",
                completed, chunkProfiles.Count);
            return new LlmResult { Success = false, Error = ex.Message };
        }

        async Task RunChunk(int index)
        {
            // Varsayilan 2048 tavan bu is icin yetmiyor: onlarca kolonun
            // sozlugu arti sorular tek cevaba sigmali, ustelik reasoning
            // token'lari da ayni butceden dusuyor.
            var text = await _model.GenerateAsync(
                BuildDictionaryPrompt(chunkProfiles[index], allTableNames, chunkProfiles.Count),
                temperature: 0.1, maxTokens: 16000, cancellationToken: ct);

            // Ayri indeksler: kilit gerekmiyor.
            parts[index] = ExtractJson(text);
            explanations[index] = ExtractExplanation(text);
        }
    }

    /// <summary>
    /// Ayni anda kac sozluk cagrisi kosacagi.
    ///
    /// Dort, iki riskin arasi: sirayla gitmek yirmi parcalik bir semada
    /// analizi ceyrek saate cikariyor, sinirsiz paralellik ise Azure'un
    /// dakikalik kotasini (429) tek hamlede tuketiyor. Kota yine de dolarsa
    /// <c>LlmClient</c> Retry-After'a uyup bekliyor; sonuc yavaslar, patlamaz.
    /// </summary>
    public const int MaxParallelChunks = 4;

    /// <summary>
    /// Kullanicinin dogal dil sorusunu, semantik sozluge bakarak analiz
    /// parametrelerine cevirir.
    /// </summary>
    /// <param name="conversation">
    /// Onceki turlarin prompt'a hazir metni (bkz.
    /// <c>ConversationContext.Render</c>). Bos gecilirse prompt'ta konusma
    /// basligi hic acilmaz ve model tek cumleyi gorur — eski davranis.
    ///
    /// Bu parametre olmadan model, kendi sordugu soruya gelen cevabi anlamsiz
    /// bir yeni soru saniyordu: kullanici "import tablosundan bakman
    /// yeterliydi" yazdiginda ortada bakilacak bir onceki tur yoktu.
    /// </param>
    public async Task<LlmResult> TranslateQuestionAsync(
        string question, string dictionaryJson, string schemaSummary,
        string conversation = "", CancellationToken ct = default)
    {
        if (_model is null) return NotConfigured();

        _logger.LogInformation("Soru çevriliyor: {Question}", question);

        try
        {
            // Bugunun tarihi prompt'a giriyor: "bu yil", "gecen ay", "son 3
            // ay" gibi ifadeler bu olmadan tarih araligina cevrilemez.
            var text = await _model.GenerateAsync(
                BuildTranslationPrompt(question, dictionaryJson, schemaSummary, DateTime.UtcNow, conversation),
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

    /// <param name="chunkCount">
    /// Toplam parca sayisi. Birden fazlaysa prompt'a kapsam uyarisi ve butun
    /// tablo adlarinin listesi giriyor, soru tavani da dusuyor: her parca
    /// sekizer soru sorarsa kullanicinin onune elli soruluk bir form cikar.
    /// </param>
    private static string BuildDictionaryPrompt(
        string profileJson, IReadOnlyList<string> allTableNames, int chunkCount)
    {
        var chunked = chunkCount > 1;

        // Sema tek cagriya sigiyorsa hicbir sey degismiyor: asagidaki blok bos
        // kaliyor ve uretilen prompt eskisiyle birebir ayni.
        var scopeBlock = !chunked ? string.Empty : $$"""
        ## Kapsam — dikkat

        Bu şema tek seferde işlenemeyecek kadar büyük olduğu için parçalara
        bölündü. Yukarıdaki profilde YALNIZCA bu turun tabloları var ve sen
        yalnızca onların sözlüğünü çıkaracaksın.

        Aşağıdaki liste veritabanındaki bütün tabloları gösteriyor; bağlam
        içindir. 5. kuraldaki karşıt çiftleri (Import/Export, In/Out,
        Order/Offer gibi) fark edebilmen için tablo adlarını bir arada görmen
        gerekiyor. Bu listede olup profilde OLMAYAN tablolar için sözlük
        girdisi ÜRETME — onlar başka bir turda işleniyor.

        Geniş bir tablonun kolonları da turlara bölünmüş olabilir: profilde
        gördüğün kolonlar o tablonun TAMAMI olmayabilir. Tablonun ne işe
        yaradığını gördüğün kolonlara bakarak yaz, görmediklerini varsayma.

        {{string.Join("\n", allTableNames.Select(n => "- " + n))}}


        """;

        // Soru tavani: tek parcada sekiz, bolunmus semada parca basina uc.
        // Toplam tavan birlestirmede ayrica uygulaniyor.
        var questionBudget = chunked ? 3 : 8;

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

        İki şeyi baştan bil, çünkü profilde kolon kolon tekrarlanmıyor:

        - `distinctCount`, `nullCount`, `minValue`, `maxValue` tam tablodan
          değil ÖRNEK SATIRLARDAN hesaplandı. Kesin sayılar değil; büyüklük
          fikri verirler. "distinct 12" gördüğünde kolonun tam olarak 12 değeri
          olduğunu varsayma.
        - Bir kolonda `sampleValues` yoksa bu kolonun BOŞ OLDUĞU anlamına
          gelmez. Gizlilik politikası o kolondan örnek almaya izin vermiyor
          demektir; kolon dolu olabilir. Böyle kolonlarda adına ve tipine
          bakarak karar ver.

        ```json
        {{profileJson}}
        ```

        {{scopeBlock}}## Sözlük kuralları
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
        geçerli. En fazla {{questionBudget}} soru sor; çözemediğin tablo ya da
        kolon yoksa `questions` boş kalsın.

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
        string question, string dictionaryJson, string schemaSummary, DateTime today,
        string conversation = "")
    {
        // Konusma bloğu bos gecilebiliyor. Bos dize verildiginde araya iki bos
        // satir bile girmiyor: "## Onceki konusma" basligi altinda hicbir sey
        // olmayan bir prompt, modele olmayan bir gecmisi arattirir.
        var conversationBlock = string.IsNullOrWhiteSpace(conversation)
            ? string.Empty
            : conversation.TrimEnd() + "\n\n";

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

        `relationships` ise modelin yorumu değil, veritabanından okunmuş ve
        değer örtüşmesiyle doğrulanmış ÖLÇÜMDÜR. Her kayıt bir tabloyu
        diğerine bağlayan gerçek bir yolu gösterir: `fromTable`/`fromColumns`
        ile `toTable`/`toColumns` eşleşen kolonları, `labelColumn` ise hedef
        tabloda kodun okunabilir karşılığını tutan kolonu söyler.

        `codeValues` de ölçümdür: az sayıda ayrık değeri olan kolonların
        veritabanından okunmuş gerçek değerleri. Bir kolonun hangi kodları
        tuttuğunu buradan öğrenirsin; filtre yazarken değer uydurmak yerine
        buraya bak.

        ```json
        {{dictionaryJson}}
        ```

        {{conversationBlock}}## Kullanıcının sorusu
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
        11. Yukarıda "## Önceki konuşma" bölümü varsa ONU ÖNCE OKU. Kullanıcının
            şu anki mesajı, senin bir önceki turda sorduğun soruya verilmiş
            cevap olabilir; öyleyse asıl soru önceki turda yazılıdır ve bu mesaj
            yalnızca eksik parçayı tamamlar. İkisini birleştirip TAM soruyu
            cevapla — mesajı tek başına yeni bir soru sayma.
        12. Aynı soruyu ikinci kez sorma. Önceki turda bir şey sorduysan ve
            kullanıcı cevap verdiyse o cevabı KULLAN. Cevap hâlâ yetmiyorsa
            BAŞKA bir şey sor; aynı cümleyi tekrarlamak konuşmayı kilitler ve
            kullanıcının çıkışı kalmaz.
        13. Devam sorularında önceki turun parametrelerini temel al. "Peki
            geçen yıl?", "bunu müşteri bazında göster", "grafiği pasta yap"
            gibi mesajlar sıfırdan yeni bir analiz değil, en son turun ÜZERİNE
            yapılan değişikliktir: değişmeyen alanları (tablo, join'ler,
            kırılım, ölçüm) olduğu gibi taşı, yalnızca istenen alanı değiştir.

        ## Birden fazla tablo — `joins`

        Sorunun cevabı tek tabloda yoksa `joins` ile zincir kur. Her adım bir
        öncekine `from` ile bağlanır; ilk adım `"from": "base"` der.

        ```json
        "joins": [
          { "as": "musteri", "from": "base",    "table": "dbo.Musteriler" },
          { "as": "ulke",    "from": "musteri", "table": "dbo.Ulkeler" }
        ]
        ```

        Kurallar:

        J1. YALNIZCA `relationships` listesinde GERÇEKTEN bulunan bir bağlantı
            istenebilir. Bağlantı yoksa join kurma — iki tablo arasında yol
            olmadığını `description` ile söyle. Eşleşen kolonları sen yazmazsın,
            ölçülmüş kayıttan okunur.
        J2. İki tablo arasında birden fazla bağlantı varsa `via` ile hangisini
            kastettiğini söyle: `"via": "MusteriId"`. Söylemezsen sorgu
            çalışmaz; tahmin edilmez.
        J3. Gerekmiyorsa join isteme. Tek tabloyla cevaplanan soruya join
            eklemek sonucu bozmaz ama yavaşlatır.
        J4. Kod yerine ADI göster. Bağlandığın tablonun `labelColumn`'u varsa
            kırılımı ona göre yap — kullanıcı müşteri numarası değil müşteri
            adı görmek ister.
        J5. Bağlanılan tablodan bir kolona atıf yaparken tablo adıyla nitele:
            `"dbo.Musteriler.Ad"`. Niteleme yoksa kolon taban tabloda aranır.
        J6. En fazla 8 adım.
        J7. AYNI tabloya birden fazla kez bağlanabilirsin. Bir parametre/kod
            tablosu çoğu zaman tek başına birden fazla şeyi tutar — para
            birimleri, ödeme tipleri, durum kodları hepsi aynı tabloda, bir
            ayırt edici kolonla ayrılmış. Böyle bir durumda her bağlantıya
            AYRI bir `as` adı ver ve `filter` ile hangi grubu istediğini söyle:

        ```json
        "joins": [
          { "as": "parabirimi", "from": "base", "table": "dbo.Parametreler",
            "via": "ParaBirimiKodu", "filter": { "Tip": "CUR" } },
          { "as": "odemetipi",  "from": "base", "table": "dbo.Parametreler",
            "via": "OdemeTipiKodu", "filter": { "Tip": "PAY" } }
        ]
        ```

            `filter` içindeki değerleri uydurma — sözlükteki `codeValues`
            listesinde o kolon için hangi değerler yazıyorsa onlardan birini
            kullan. Kolon `codeValues`'ta yoksa `filter` kullanma.

        ## Bire-çok tablolar — `preAggregate`

        Bir tabloda taban tablonun her satırı için BİRDEN FAZLA satır varsa
        (fatura → fatura kalemleri gibi) o tabloyu doğrudan bağlamak satırları
        çoğaltır ve bütün sayılar şişer. Böyle bir tablodan ölçü almak
        istiyorsan ön toplama iste:

        ```json
        { "as": "kalem", "from": "base", "table": "dbo.FaturaKalemleri",
          "preAggregate": { "aggregation": "sum", "column": "Tutar" } }
        ```

        Bu, kalemleri fatura başına önceden toplar ve sonucu tek satır olarak
        bağlar; böylece "kaç fatura" ile "kalem tutarı toplamı" aynı sorguda
        ikisi de doğru çıkar. Ön toplanmış sonuca `as` adıyla atıf yapılır:
        `"target_column": "kalem"`.

        Emin değilsen ön toplama iste — çoğaltılmış satırlardan çıkan sayı
        sessizce yanlış olur, ön toplama ise hiçbir şeyi bozmaz.

        ## Toplulaştırma sonrası koşul — `having`

        "Toplamı 1 milyonu geçen müşteriler", "5'ten fazla siparişi olanlar"
        gibi sorularda koşul tek tek satırlara değil, HESAPLANAN ÖLÇÜYE
        uygulanır:

        ```json
        "having": { "op": ">", "value": 1000000 }
        ```

        Koşul her zaman bu sorgunun kendi `aggregation`'ına uygulanır; ayrıca
        bir alan yazman gerekmiyor. `op` şunlardan biri: `>`, `>=`, `<`, `<=`,
        `=`, `<>`. Değer sayı olmalı.

        `filters` ile karıştırma — ikisi farklı soruları cevaplar:

        - `filters` satırları toplamadan ÖNCE eler: "tutarı 1 milyondan büyük
          FATURALARI topla".
        - `having` grupları toplandıktan SONRA eler: "toplamı 1 milyonu geçen
          MÜŞTERİLERİ getir".

        Bir müşterinin tek tek faturaları küçük ama toplamı büyük olabilir;
        iki soru aynı veride neredeyse hiçbir zaman aynı sonucu vermez.

        ## Kırılımın üzerinde hesap — `window`

        Grupların ÜZERİNDE hesaplanan ek bir kolon. Toplulaştırmanın
        yapamadığı şeyler için:

        ```json
        "window": { "function": "running_total" }
        ```

        - `running_total`  : birikimli toplam ("kümülatif ciro", "yıl başından
                             beri toplam").
        - `moving_average` : hareketli ortalama. Kaç dönem olduğunu `periods`
                             ile yaz: `{"function": "moving_average", "periods": 3}`.
                             En az 2 olmalı.
        - `total`          : genel toplam; her satırda aynı çıkar, pay
                             hesaplamak için.
        - `rank` / `dense_rank` / `row_number` : sıra numarası, ölçüye göre.

        `running_total` ve `moving_average` KIRILIM SIRASINA göre hesaplanır:
        `group_by`'ın ilk kolonu zaman kolonu olmalı, yoksa birikim anlamsız
        olur. Bu ikisinde sonuç ölçüye göre değil zamana göre sıralanır ve
        `limit` uygulanmaz — kesilmiş bir birikim doğru görünür ama yanlıştır.
        Zaman aralığını daraltmak istiyorsan `filters` kullan.

        ## Aynı soru birden fazla tabloda — `union`

        Aynı şey iki ayrı tabloda tutuluyorsa (ithalat/ihracat, gelen/giden,
        arşiv/canlı) ve kullanıcı ikisini BİR ARADA görmek istiyorsa:

        ```json
        "union": {
          "label": "İthalat",
          "with": [
            { "table": "dbo.Ihracat", "label": "İhracat",
              "group_by": ["Country"], "target_column": "Amount" }
          ]
        }
        ```

        `label` taban tablonun (yani `target_table`'ın) etiketidir; her dal
        grafikte ayrı bir seri olur. Dalın `group_by` ve `target_column`'unu
        YALNIZCA kolon adları taban tablodakinden farklıysa yaz; aynıysa hiç
        yazma. Dalın kendi `filters`'ı olabilir.

        `joins` ile karıştırma. Join tabloları YAN YANA koyar — bir faturanın
        müşterisi, müşterinin ülkesi. Birleşim ALT ALTA koyar — ithalat
        satırları, altına ihracat satırları. Soru "ikisini karşılaştır" ise
        birleşim, "şunun şusu" ise join.

        Kurallar: birleşimde `group_by` zorunlu, her dalın etiketi farklı
        olmalı ve `union` ile birlikte `joins`, `having`, `window`
        kullanılamaz — bunlardan birine ihtiyaç varsa soruyu tek kaynak
        üzerinden sor.

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
          "joins": [
            { "as": "musteri", "from": "base", "table": "dbo.Musteriler",
              "via": "isteğe bağlı — birden fazla bağlantı varsa hangisi",
              "preAggregate": { "aggregation": "sum", "column": "Tutar" } }
          ],
          "target_column": "kolon_adı veya null",
          "feature_columns": ["kolon1", "kolon2"],
          "filters": { "kolon_adı": "değer | [değer, ...] | { \"gte\": \"...\", \"lt\": \"...\" }" },
          "having": { "op": ">", "value": 1000 },
          "window": { "function": "running_total|moving_average|total|rank|dense_rank|row_number", "periods": 3 },
          "union": { "label": "İthalat", "with": [ { "table": "dbo.Ihracat", "label": "İhracat" } ] },
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

        `joins`, `having`, `window` ve `union` isteğe bağlıdır: soru
        gerektirmiyorsa hiç yazma. Gereksiz yere eklemek sonucu bozmaz ama
        sorguyu yavaşlatır, `union` söz konusu olduğunda ise grafiğe olmayan
        bir seri ekler.

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
