using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.Measurement.Evaluation;
using Grafirio.Measurement.Semantic;
using Grafirio.SemanticEval.Data;
using Grafirio.SemanticEval.Scoring;

namespace Grafirio.SemanticEval.Tests;

public class RelationshipScoringTests
{
    private static readonly SemanticGold Gold = new()
    {
        Dataset = "t",
        Relationships =
        [
            new GoldEdge { From = "dbo.Orders.CustomerId", To = "dbo.Customers.Id", Kind = "fk" },
            new GoldEdge { From = "dbo.Orders.ShipperId", To = "dbo.Companies.Id", Kind = "name-role" },
            new GoldEdge { From = "dbo.Invoice.RefNo", To = "dbo.Orders.OrderNo", Kind = "abbreviation" }
        ],
        NonRelationships = [new GoldEdge { From = "dbo.Orders.CarrierNo", To = "dbo.Companies.Id", Reason = "tesaduf" }]
    };

    private static RelationshipProfile Edge(string from, string fromColumn, string to, string toColumn,
        string confidence = "high") => new()
    {
        FromTable = from, FromColumns = [fromColumn], ToTable = to, ToColumns = [toColumn], Confidence = confidence
    };

    [Fact]
    public void Dogru_yanlis_ve_tuzak_ayri_sayiliyor()
    {
        var result = RelationshipScoring.Score("t", Gold,
        [
            Edge("dbo.Orders", "CustomerId", "dbo.Customers", "Id"),
            Edge("[dbo].[Orders]", "ShipperId", "dbo.Companies", "Id", "medium"),
            Edge("dbo.Orders", "CarrierNo", "dbo.Companies", "Id"),          // tuzak
            Edge("dbo.Orders", "Total", "dbo.Invoice", "Tutar")              // altinda yok
        ]);

        Assert.Equal(2, result.TruePositives);
        Assert.Equal(2, result.FalsePositives);
        Assert.Equal(1, result.FalseNegatives);
        Assert.Equal(1, result.TrapHits);
        Assert.Equal((1, 1), result.ByKind["fk"]);
        Assert.Equal((0, 1), result.ByKind["abbreviation"]);

        // 3 yuksek guvenli kenardan 1'i dogru.
        Assert.Equal(3, result.HighConfidence);
        Assert.Equal(1, result.HighConfidenceCorrect);
        Assert.Contains(result.Cases, c => c.Group == "t/tuzak" && !c.Success && c.Message!.Contains("tesaduf"));
    }

    [Fact]
    public void Ters_yon_ayni_kenar_sayilmiyor()
    {
        var result = RelationshipScoring.Score("t", Gold, [Edge("dbo.Customers", "Id", "dbo.Orders", "CustomerId")]);
        Assert.Equal(0, result.TruePositives);
        Assert.Equal(1, result.FalsePositives);
    }

    [Fact]
    public void Ozet_mikro_ortalama_ve_set_basina_f1_veriyor()
    {
        var a = RelationshipScoring.Score("a", Gold, [Edge("dbo.Orders", "CustomerId", "dbo.Customers", "Id")]);
        var metrics = RelationshipScoring.Summarize([a]).ToDictionary(m => m.Name);

        Assert.Equal(1.0, metrics["precision"].Value);
        Assert.Equal(1 / 3d, metrics["recall"].Value, 6);
        Assert.Equal(0.5, metrics["f1"].Value, 6);
        Assert.True(metrics.ContainsKey("f1@a"));
        Assert.Equal(1.0, metrics["recall.fk"].Value);
        Assert.Equal("higher", metrics["f1"].Direction);
        Assert.Equal("lower", metrics["trap_hits"].Direction);
    }
}

public class SchemaScoringTests
{
    private static DatabaseProfile Profile() => new()
    {
        Tables =
        [
            new TableProfile
            {
                Schema = "dbo", TableName = "L_INT_ExportReference",
                Columns =
                [
                    new ColumnProfile { ColumnName = "FreightAmount", DataType = "decimal", SamplingDecision = "Allowed" },
                    new ColumnProfile { ColumnName = "LoadingDate", DataType = "date", SamplingDecision = "Allowed" },
                    new ColumnProfile { ColumnName = "DriverNationalId", DataType = "char", SamplingDecision = "Denied" },
                    new ColumnProfile { ColumnName = "TaxNumber", DataType = "varchar", SamplingDecision = "NeedsConsent" }
                ]
            }
        ]
    };

    private static readonly SemanticGold Gold = new()
    {
        Dataset = "t",
        Tables = new() { ["dbo.L_INT_ExportReference"] = new GoldTable { Concepts = ["ihracat"], Forbidden = ["ithalat"] } },
        Columns = new()
        {
            ["dbo.L_INT_ExportReference.FreightAmount"] = new GoldColumn { Role = ["measure"] },
            ["dbo.L_INT_ExportReference.LoadingDate"] = new GoldColumn { Role = ["date"] },
            ["dbo.L_INT_ExportReference.DriverNationalId"] = new GoldColumn { Role = ["identifier"], Sensitive = true },
            ["dbo.L_INT_ExportReference.TaxNumber"] = new GoldColumn { Role = ["identifier"], Sensitive = true }
        }
    };

    private static JsonObject Dictionary(string purpose, string freightRole) => JsonNode.Parse($$"""
        {
          "tables": [ { "name": "dbo.L_INT_ExportReference", "purpose": "{{purpose}}", "synonyms": [] } ],
          "columns": [
            { "table": "dbo.L_INT_ExportReference", "column": "FreightAmount", "role": "{{freightRole}}", "meaning": "Navlun" },
            { "table": "dbo.L_INT_ExportReference", "column": "LoadingDate", "role": "date", "meaning": "unknown" },
            { "table": "dbo.L_INT_ExportReference", "column": "DriverNationalId", "role": "identifier", "meaning": "TC" },
            { "table": "dbo.L_INT_ExportReference", "column": "TaxNumber", "role": "other", "meaning": "Vergi" }
          ],
          "questions": []
        }
        """)!.AsObject();

    [Fact]
    public void Hassas_kolon_korumasi_llm_olmadan_olculuyor()
    {
        var result = SchemaScoring.Score("t", Gold, Profile(), dictionary: null);
        var metrics = result.Metrics.ToDictionary(m => m.Name);

        Assert.Equal(0.5, metrics["sensitive.recall"].Value);
        Assert.DoesNotContain("role.accuracy", metrics.Keys);
        Assert.Contains(result.Cases, c => c.Name.EndsWith("TaxNumber") && !c.Success);
    }

    [Fact]
    public void Rol_ve_tablo_kavrami_puanlaniyor()
    {
        var result = SchemaScoring.Score("t", Gold, Profile(), Dictionary("Ihracat sevkiyatları", "dimension"));
        var metrics = result.Metrics.ToDictionary(m => m.Name);

        // FreightAmount yanlis (dimension), TaxNumber yanlis (other): 2/4.
        Assert.Equal(0.5, metrics["role.accuracy"].Value);
        Assert.Equal(0, metrics["role.recall.measure"].Value);
        Assert.Equal(1, metrics["role.recall.date"].Value);
        Assert.Equal(0.75, metrics["meaning.coverage"].Value);
        // "Ihracat" buyuk I ile yazilmis; Turkce katlama ile yine eslesmeli.
        Assert.Equal(1, metrics["table.concept_accuracy"].Value);
        Assert.Equal(0, metrics["table.opposite_confusions"].Value);
    }

    [Fact]
    public void Karsit_kavram_karisikligi_yakalaniyor()
    {
        var result = SchemaScoring.Score("t", Gold, Profile(), Dictionary("İhracat ve ithalat referansları", "measure"));
        var metrics = result.Metrics.ToDictionary(m => m.Name);

        Assert.Equal(1, metrics["table.opposite_confusions"].Value);
        Assert.Contains(result.Cases, c => c.Group == "t/tablo" && !c.Success && c.Message!.Contains("ithalat"));
    }

    [Theory]
    [InlineData("İHRACAT", "ihracat")]
    [InlineData("Işık Çağrı Ünlü", "isik cagri unlu")]
    [InlineData("EXPORT", "export")]
    public void Turkce_katlama(string input, string expected) => Assert.Equal(expected, SchemaScoring.Fold(input));
}

public class MappingScoringTests
{
    private static SemanticQuestion Answerable(string id, string table) => new()
    {
        Id = id, Kind = "answerable", Question = "q",
        Expect = new EvalExpectation
        {
            Params = new JsonObject { ["target_table"] = table, ["aggregation"] = "count" }
        }
    };

    private static TranslationOutcome Done(string table, string aggregation = "count", string title = "x") =>
        new("q", "completed", JsonNode.Parse($$"""{"target_table":"{{table}}","aggregation":"{{aggregation}}","chart_title":"{{title}}"}"""), null);

    [Fact]
    public void Esleme_tablo_ve_tam_dogrulugu_ayri_olcuyor()
    {
        var result = MappingScoring.Score(
        [
            ("d", Answerable("a1", "dbo.Orders"), [Done("dbo.Orders", title: "A"), Done("dbo.Orders", title: "B")]),
            ("d", Answerable("a2", "dbo.Orders"), [Done("dbo.Orders", "sum"), Done("dbo.Customers")])
        ]);

        var metrics = result.MappingMetrics.ToDictionary(m => m.Name);
        Assert.Equal(0.5, metrics["accuracy"].Value);        // 2/4 deneme tam dogru
        Assert.Equal(0.75, metrics["table.accuracy"].Value); // 3/4 dogru tablo
        Assert.Equal(0.5, metrics["pass.rate"].Value);
        // a1: baslik farkli ama cekirdek parametre ayni → tutarli. a2: iki farkli cevap.
        Assert.Equal(0.75, metrics["paraphrase.consistency"].Value);
    }

    [Fact]
    public void Bilinmeyende_sormak_dogru_uydurmak_yanlis()
    {
        var unknown = new SemanticQuestion
        {
            Id = "u1", Kind = "unknown", UnknownType = "missing-column", Question = "q",
            Expect = new EvalExpectation { Status = "clarification" }
        };

        var result = MappingScoring.Score(
        [
            ("d", unknown, [
                new TranslationOutcome("q1", "clarification", null, null),
                new TranslationOutcome("q2", "rejected", null, "Kolon yok"),
                Done("dbo.Orders")
            ]),
            ("d", Answerable("a1", "dbo.Orders"), [new TranslationOutcome("q", "clarification", null, null)])
        ]);

        var metrics = result.UnknownMetrics.ToDictionary(m => m.Name);
        Assert.Equal(2 / 3d, metrics["abstention.recall"].Value, 6);
        Assert.Equal(1 / 3d, metrics["hallucination.rate"].Value, 6);
        Assert.Equal(1.0, metrics["false_abstention.rate"].Value);
        Assert.Equal(2 / 3d, metrics["abstention.recall.missing-column"].Value, 6);
        Assert.False(Assert.Single(result.UnknownCases).Success);
    }
}

public class DatasetTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "evals", "semantic");

    [Fact]
    public void Gercek_veri_setleri_gecerli_yukleniyor()
    {
        var datasets = SemanticDataset.LoadAll(Root);

        Assert.Contains(datasets, d => d.Name == "eticaret");
        Assert.Contains(datasets, d => d.Name == "lojistik");
        foreach (var dataset in datasets)
        {
            Assert.NotEmpty(dataset.Gold.Relationships);
            Assert.Contains(dataset.Questions.Questions, q => q.Kind == "unknown");
            Assert.All(dataset.Questions.Questions.Where(q => q.Kind == "answerable"),
                q => Assert.False(string.IsNullOrWhiteSpace(q.Gold?.Sql), $"{q.Id} altin SQL'siz"));
        }
    }

    [Fact]
    public void Go_satirlari_batchleri_ayiriyor_yorumlar_atlaniyor()
    {
        var batches = DatasetInstaller.Batches("-- yorum\nCREATE TABLE a (x int);\nGO\n\n  go  \nSELECT 1;\n-- son\nGO\n-- yalniz yorum\n").ToList();

        Assert.Equal(2, batches.Count);
        Assert.StartsWith("-- yorum", batches[0]);
        Assert.Equal("SELECT 1;\n-- son", batches[1].Replace("\r", ""));
    }
}
