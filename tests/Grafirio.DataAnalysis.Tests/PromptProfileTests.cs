using System.Text.Json;
using System.Text.Json.Serialization;
using Grafirio.DataAnalysis.Api.Features.Profile;

namespace Grafirio.DataAnalysis.Tests;

/// <summary>
/// Modele giden profilin bicimi.
///
/// Iki yonlu bir kisit: gereksiz alanlar CIKMALI (maliyet ve baglam penceresi
/// icin), ama modelin karar verdigi alanlar KALMALI. `qualified` dusmesi
/// ozellikle sinsi olurdu — model sozlukteki tablo adini oradan yaziyor ve
/// yanlis ad, ceviri aninda hicbir tablonun bulunamamasi demek.
/// </summary>
public class PromptProfileTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static DatabaseProfile Sample() => new()
    {
        DatabaseName = "Test",
        Tables =
        [
            new TableProfile
            {
                Schema = "dbo",
                TableName = "Faturalar",
                ApproximateRowCount = 1000,
                Columns =
                [
                    new ColumnProfile
                    {
                        ColumnName = "Id",
                        DataType = "int",
                        IsPrimaryKey = true,
                        StatsFromSample = true,
                        SamplingDecision = "Allowed",
                        SamplingNote = "Bu kolon hassas değil, örnek alınabilir."
                    },
                    new ColumnProfile
                    {
                        ColumnName = "Tip",
                        DataType = "varchar",
                        DistinctCount = 30,
                        StatsFromSample = true,
                        SamplingDecision = "Allowed",
                        SampleValues = Enumerable.Range(1, 20).Select(i => $"K{i}").ToList()
                    },
                    new ColumnProfile
                    {
                        ColumnName = "TcKimlik",
                        DataType = "varchar",
                        SamplingDecision = "Blocked",
                        SamplingNote = "Kimlik numarası olabilir; örnek alınmadı."
                    }
                ]
            }
        ]
    };

    private static JsonElement Serialize(DatabaseProfile profile) =>
        JsonSerializer.Deserialize<JsonElement>(PromptProfile.Serialize(profile, Options));

    private static JsonElement Column(JsonElement root, int index) =>
        root.GetProperty("tables")[0].GetProperty("columns")[index];

    [Fact]
    public void Kolon_basina_tekrar_eden_alanlar_cikariliyor()
    {
        // Bunlar prompt'ta bir kez anlatiliyor; sekiz yuz kolonda tekrar
        // etmelerinin karsiligi yok.
        var json = PromptProfile.Serialize(Sample(), Options);

        Assert.DoesNotContain("samplingNote", json);
        Assert.DoesNotContain("samplingDecision", json);
        Assert.DoesNotContain("statsFromSample", json);
    }

    [Fact]
    public void Nitelenmis_tablo_adi_korunuyor()
    {
        // Model sözlükteki `tables[].name` alanını buradan yazıyor.
        var root = Serialize(Sample());

        Assert.Equal("dbo.Faturalar", root.GetProperty("tables")[0].GetProperty("qualified").GetString());
    }

    [Fact]
    public void Ornek_degerler_tavana_kirpiliyor()
    {
        var values = Column(Serialize(Sample()), 1).GetProperty("sampleValues");

        Assert.Equal(PromptProfile.MaxSampleValues, values.GetArrayLength());
        Assert.Equal("K1", values[0].GetString());
    }

    [Fact]
    public void Ornek_alinamamis_kolonda_bos_liste_yazilmiyor()
    {
        // Bos bir dizi kolon basina birkac token; "neden bos" bilgisi zaten
        // prompt'ta.
        Assert.False(Column(Serialize(Sample()), 2).TryGetProperty("sampleValues", out _));
    }

    [Fact]
    public void Birincil_anahtar_yalnizca_dogruyken_yaziliyor()
    {
        var root = Serialize(Sample());

        Assert.True(Column(root, 0).GetProperty("isPrimaryKey").GetBoolean());
        Assert.False(Column(root, 1).TryGetProperty("isPrimaryKey", out _));
    }

    [Fact]
    public void Modelin_karar_verdigi_alanlar_kaliyor()
    {
        var column = Column(Serialize(Sample()), 1);

        Assert.Equal("Tip", column.GetProperty("columnName").GetString());
        Assert.Equal("varchar", column.GetProperty("dataType").GetString());
        Assert.Equal(30, column.GetProperty("distinctCount").GetInt32());
    }

    [Fact]
    public void Profil_nesnesi_degistirilmiyor()
    {
        // codeValues ve profileStats hâlâ tam listeyi okuyor; kirpma yalnizca
        // modele giden kopyada olmali.
        var profile = Sample();

        PromptProfile.Serialize(profile, Options);

        Assert.Equal(20, profile.Tables[0].Columns[1].SampleValues.Count);
        Assert.NotNull(profile.Tables[0].Columns[0].SamplingNote);
    }
}
