using Microsoft.Data.SqlClient;

namespace Grafirio.DummyData.Generator;

/// <summary>
/// Ornek veritabani uretiminin tum ayarlari. Komut satirindan veya ortam
/// degiskenlerinden gelir; hicbiri koda gomulu degildir, boylece ayni generator
/// ile farkli boyutlarda ve farkli isimlerde veritabanlari uretilebilir.
/// </summary>
public sealed class GeneratorOptions
{
    public string Server { get; set; } = "localhost,1435";
    public string Database { get; set; } = "GrafirioECommerce";
    public string User { get; set; } = "sa";
    public string Password { get; set; } = "";
    public string Scale { get; set; } = "medium";
    public int Seed { get; set; } = 42;

    /// <summary>Veritabani varsa once tamamen silinir.</summary>
    public bool Reset { get; set; }

    /// <summary>Onay sormadan calis (CI / script kullanimi).</summary>
    public bool AssumeYes { get; set; }

    /// <summary>Sadece semayi olustur, veri uretme.</summary>
    public bool SchemaOnly { get; set; }

    /// <summary>Salt-okunur giris de olusturulsun mu (Grafirio baglantisi icin).</summary>
    public bool CreateReadonlyLogin { get; set; }
    public string ReadonlyUser { get; set; } = "grafirio_readonly";
    public string ReadonlyPassword { get; set; } = "";

    public int CategoryCount { get; set; }
    public int ProductCount { get; set; }
    public int CustomerCount { get; set; }
    public int OrderCount { get; set; }
    public int DiscountCount { get; set; }

    /// <summary>Siparislerin yayildigi gecmis gun sayisi.</summary>
    public int HistoryDays { get; set; } = 730;

    public string MasterConnectionString => BuildConnectionString("master");
    public string DatabaseConnectionString => BuildConnectionString(Database);

    private string BuildConnectionString(string database) =>
        new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = database,
            UserID = User,
            Password = Password,
            TrustServerCertificate = true,
            ConnectTimeout = 30,
            CommandTimeout = 0
        }.ConnectionString;

    /// <summary>Onceden tanimli buyukluk profilleri.</summary>
    private static readonly Dictionary<string, (int Cat, int Prod, int Cust, int Ord, int Disc)> Profiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["tiny"] = (8, 100, 50, 300, 10),
            ["small"] = (15, 1_000, 500, 3_000, 20),
            ["medium"] = (25, 8_000, 3_000, 25_000, 40),
            ["large"] = (30, 30_000, 10_000, 100_000, 60),
            ["xl"] = (40, 67_500, 25_000, 250_000, 80),
        };

    public static IEnumerable<string> ScaleNames => Profiles.Keys;

    /// <summary>
    /// Argumanlari ayristirir. Yardim istendiginde veya arguman hatali oldugunda
    /// null doner; hata mesaji bos string ise kullanici yardim istemistir.
    /// </summary>
    public static GeneratorOptions Parse(string[] args, out string error)
    {
        error = null;
        var options = new GeneratorOptions();
        var overrides = new Dictionary<string, int>();

        // Sifreler once ortam degiskeninden okunur; komut satirinda sifre birakmak zorunda kalmayalim.
        options.Password = Environment.GetEnvironmentVariable("SAMPLEDB_PASSWORD") ?? "";
        options.ReadonlyPassword = Environment.GetEnvironmentVariable("SAMPLEDB_READONLY_PASSWORD") ?? "";

        foreach (var arg in args)
        {
            if (arg is "--help" or "-h" or "/?")
            {
                error = "";
                return null;
            }

            var (key, value) = SplitArg(arg);
            switch (key)
            {
                case "--server": options.Server = value; break;
                case "--database":
                case "--db": options.Database = value; break;
                case "--user": options.User = value; break;
                case "--password": options.Password = value; break;
                case "--scale": options.Scale = value; break;
                case "--seed": options.Seed = ParseInt(value, key, ref error); break;
                case "--history-days": options.HistoryDays = ParseInt(value, key, ref error); break;
                case "--categories": overrides["cat"] = ParseInt(value, key, ref error); break;
                case "--products": overrides["prod"] = ParseInt(value, key, ref error); break;
                case "--customers": overrides["cust"] = ParseInt(value, key, ref error); break;
                case "--orders": overrides["ord"] = ParseInt(value, key, ref error); break;
                case "--discounts": overrides["disc"] = ParseInt(value, key, ref error); break;
                case "--reset": options.Reset = true; break;
                case "--yes":
                case "-y": options.AssumeYes = true; break;
                case "--schema-only": options.SchemaOnly = true; break;
                case "--readonly-login": options.CreateReadonlyLogin = true; break;
                case "--readonly-user": options.ReadonlyUser = value; break;
                case "--readonly-password": options.ReadonlyPassword = value; break;
                default:
                    error = $"Bilinmeyen arguman: {arg}";
                    return null;
            }

            if (error != null) return null;
        }

        if (!Profiles.TryGetValue(options.Scale, out var profile))
        {
            error = $"Bilinmeyen olcek: {options.Scale}. Secenekler: {string.Join(", ", Profiles.Keys)}";
            return null;
        }

        options.CategoryCount = overrides.GetValueOrDefault("cat", profile.Cat);
        options.ProductCount = overrides.GetValueOrDefault("prod", profile.Prod);
        options.CustomerCount = overrides.GetValueOrDefault("cust", profile.Cust);
        options.OrderCount = overrides.GetValueOrDefault("ord", profile.Ord);
        options.DiscountCount = overrides.GetValueOrDefault("disc", profile.Disc);

        if (string.IsNullOrWhiteSpace(options.Database))
        {
            error = "Veritabani adi bos olamaz.";
            return null;
        }

        if (string.IsNullOrEmpty(options.Password))
        {
            error = "SA sifresi yok. --password ile verin veya SAMPLEDB_PASSWORD ortam degiskenini ayarlayin.";
            return null;
        }

        if (options.CreateReadonlyLogin && string.IsNullOrEmpty(options.ReadonlyPassword))
        {
            error = "--readonly-login icin sifre gerekli: --readonly-password veya SAMPLEDB_READONLY_PASSWORD.";
            return null;
        }

        return options;
    }

    private static (string Key, string Value) SplitArg(string arg)
    {
        var index = arg.IndexOf('=');
        return index < 0 ? (arg, "") : (arg[..index], arg[(index + 1)..]);
    }

    private static int ParseInt(string value, string key, ref string error)
    {
        if (int.TryParse(value, out var parsed) && parsed >= 0) return parsed;
        error = $"{key} sayisal bir deger bekliyor, gelen: '{value}'";
        return 0;
    }

    public static string HelpText => """
        Grafirio ornek veritabani ureteci

        Kullanim:
          dotnet run -- --database=Ornek1 --scale=medium --password=... [secenekler]

        Baglanti:
          --server=<host,port>        Varsayilan: localhost,1435
          --database=<ad>             Olusturulacak veritabani adi (varsayilan: GrafirioECommerce)
          --user=<kullanici>          Varsayilan: sa
          --password=<sifre>          Yoksa SAMPLEDB_PASSWORD ortam degiskeni kullanilir

        Boyut:
          --scale=tiny|small|medium|large|xl
          --categories=N --products=N --customers=N --orders=N --discounts=N
                                      Profildeki degerleri tek tek ezmek icin
          --history-days=N            Siparislerin yayildigi gun sayisi (varsayilan: 730)
          --seed=N                    Ayni tohum ayni veriyi uretir (varsayilan: 42)

        Davranis:
          --reset                     Veritabani varsa once tamamen sil
          --schema-only               Sadece tablolari olustur, veri uretme
          --yes, -y                   Onay sorma
          --readonly-login            Salt-okunur giris de olustur (Grafirio baglantisi icin)
          --readonly-user=<ad>        Varsayilan: grafirio_readonly
          --readonly-password=<sifre> Yoksa SAMPLEDB_READONLY_PASSWORD kullanilir
          --help                      Bu yardim
        """;
}
