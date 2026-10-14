using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Application.Analysis;
using Grafirio.DataAnalysis.Api.Application.Interfaces;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Reflection;

namespace Grafirio.DataAnalysis.Tests;

public sealed class ExplicitEntityFilterTests
{
    private const string Dictionary = """
        {"tables":[{"name":"ecom.Product","purpose":"Ürün kataloğu"},{"name":"crm.Customer","purpose":"Müşteriler"}],
         "columns":[
           {"table":"ecom.Product","column":"Name","role":"dimension","meaning":"Ürün adı","synonyms":["ürün adı","isim"]},
           {"table":"ecom.Product","column":"Sku","role":"dimension","meaning":"Ürün stok kodu (SKU)","synonyms":["sku","stok kodu"]},
           {"table":"ecom.Product","column":"ProductId","role":"identifier","isPrimaryKey":true,"meaning":"Ürünün benzersiz kimliği"},
           {"table":"crm.Customer","column":"DisplayName","role":"dimension","meaning":"Müşteri adı","synonyms":["müşteri adı"]},
           {"table":"crm.Customer","column":"CustomerCode","role":"dimension","meaning":"Müşteri kodu"}]}
        """;
    private const string Question = "Şarjlı tablet 7863 adlı ürünü bul";
    private const string Wrong = """{"analysis_type":"list","target_table":"ecom.Product","filters":{"ecom.Product.Sku":"7863"}}""";
    private const string Correct = """{"analysis_type":"list","target_table":"ecom.Product","filters":{"ecom.Product.Name":"Şarjlı tablet 7863"}}""";
    private static LlmAnalysisService Service(Mock<ILlmClient> client) => new(client.Object, NullLogger<LlmAnalysisService>.Instance);
    private static Mock<ILlmClient> Model(params string[] responses)
    {
        var client = new Mock<ILlmClient>(MockBehavior.Strict);
        client.SetupGet(x => x.IsConfigured).Returns(true);
        var sequence = client.SetupSequence(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()));
        foreach (var response in responses) sequence.ReturnsAsync(response);
        return client;
    }

    [Theory]
    [InlineData("Şarjlı tablet 7863 adlı ürünü bul")]
    [InlineData("Şarjlı tablet 7863 isimli ürünü bul")]
    [InlineData("Bana Şarjlı tablet 7863 adlı ürünü bul")]
    [InlineData("ürün adı Şarjlı tablet 7863 olan kaydı göster")]
    [InlineData("ŞARJLI TABLET 7863 ADLI ÜRÜNÜ BUL")]
    public void ExplicitNamedProductCannotUseSku(string question)
        => Assert.NotNull(ExplicitEntityFilterPolicy.From(question, Dictionary).Validate(Wrong));

    [Theory]
    [InlineData("ecom.Product.Name")]
    [InlineData("[ecom].[Product].[Name]")]
    [InlineData("Name")]
    [InlineData("base.Name")]
    public void DictionaryBackedNameFilterIsAccepted(string column)
    {
        var plan = JsonNode.Parse(Correct)!.AsObject();
        plan["filters"] = new JsonObject { [column] = "Şarjlı tablet 7863" };
        Assert.Null(ExplicitEntityFilterPolicy.From(Question, Dictionary).Validate(plan.ToJsonString()));
    }

    [Fact]
    public void JoinedNamesResolveThroughTheActualAlias()
    {
        var policy = ExplicitEntityFilterPolicy.From(Question, Dictionary);
        const string plan = """{"target_table":"ecom.SalesOrderLine","joins":[{"as":"product","table":"ecom.Product"}],"filters":{"product.Name":"Şarjlı tablet 7863"}}""";
        Assert.Null(policy.Validate(plan));
        Assert.NotNull(policy.Validate(plan.Replace("product.Name", "product.Sku")));
    }

    [Fact]
    public void JoinsWithoutAnExplicitAliasStillUseTheirDictionaryTable()
        => Assert.Null(ExplicitEntityFilterPolicy.From(Question, Dictionary).Validate(
            """{"target_table":"ecom.SalesOrderLine","joins":[{"table":"ecom.Product"}],"filters":{"ecom.Product.Name":"Şarjlı tablet 7863"}}"""));

    [Fact]
    public void ACorrectNameCannotBeNarrowedByAnInventedSkuOrReducedToASuffix()
    {
        var policy = ExplicitEntityFilterPolicy.From(Question, Dictionary);
        var plan = JsonNode.Parse(Correct)!.AsObject();
        plan["filters"]!["ecom.Product.Sku"] = "7863";
        Assert.NotNull(policy.Validate(plan.ToJsonString()));
        Assert.NotNull(policy.Validate(Correct.Replace("Şarjlı tablet 7863", "7863")));
        Assert.NotNull(policy.Validate(Correct.Replace("Şarjlı tablet 7863", "Şarjlı tablet")));
        Assert.Null(ExplicitEntityFilterPolicy.From("Bana 7863 adlı ürünü bul", Dictionary).Validate(Correct.Replace("Şarjlı tablet 7863", "7863")));
    }

    [Fact]
    public void AProductCategoryNameIsNotAProductName()
    {
        var dictionary = JsonNode.Parse(Dictionary)!.AsObject();
        dictionary["columns"]!.AsArray().Add(new JsonObject { ["table"] = "ecom.ProductCategory", ["column"] = "Name", ["meaning"] = "Ürün kategorisi adı", ["role"] = "dimension" });
        Assert.NotNull(ExplicitEntityFilterPolicy.From(Question, dictionary.ToJsonString()).Validate(Correct.Replace("ecom.Product", "ecom.ProductCategory")));
    }

    [Fact]
    public void LocalizedCustomNamesAreResolvedThroughDictionarySynonyms()
    {
        const string dictionary = """{"columns":[{"table":"dbo.T001","column":"C003","role":"dimension","synonyms":["ürün adı"]}]}""";
        Assert.Null(ExplicitEntityFilterPolicy.From(Question, dictionary).Validate("""{"target_table":"dbo.T001","filters":{"C003":"Şarjlı tablet 7863"}}"""));
    }

    [Fact]
    public void ExplicitSkuCannotBeReplacedByANameOrDifferentIdentifier()
    {
        var policy = ExplicitEntityFilterPolicy.From("SKU 7863 olan ürünü bul", Dictionary);
        Assert.Null(policy.Validate(Wrong));
        Assert.NotNull(policy.Validate(Correct));
        Assert.NotNull(policy.Validate(Wrong.Replace("ecom.Product.Sku", "ecom.Product.ProductId")));
    }

    [Fact]
    public void CustomerNamesAndMultipleEntitiesAreCheckedIndependently()
    {
        var policy = ExplicitEntityFilterPolicy.From("Ali 123 adlı müşteri için Şarjlı tablet 7863 adlı ürünü bul", Dictionary);
        var plan = JsonNode.Parse(Correct)!.AsObject();
        plan["joins"] = new JsonArray(new JsonObject { ["as"] = "customer", ["table"] = "crm.Customer" });
        Assert.NotNull(policy.Validate(plan.ToJsonString()));
        plan["filters"]!["customer.DisplayName"] = "Ali 123";
        Assert.Null(policy.Validate(plan.ToJsonString()));
    }

    [Fact]
    public void ClarificationAndQuestionsWithoutExplicitNameOrCodeRemainUnchanged()
    {
        Assert.Null(ExplicitEntityFilterPolicy.From(Question, Dictionary).Validate("""{"target_table":"","description":"Hangi ürün?"}"""));
        Assert.Null(ExplicitEntityFilterPolicy.From("Şarjlı tablet 7863'ün cirosu", Dictionary).Validate(Wrong));
        Assert.Null(ExplicitEntityFilterPolicy.From("Son 5 ürünü listele", "{}").Validate(Correct));
    }

    [Fact]
    public async Task ContradictoryTranslationIsRepairedOnceAndBothAttemptsAreRetained()
    {
        var model = Model(Wrong, Correct);
        var result = await Service(model).TranslateQuestionAsync(Question, Dictionary, "");
        Assert.True(result.Success);
        Assert.Equal(Correct, result.Json);
        Assert.Equal(new[] { Wrong, Correct }, result.TranslationAttempts);
        model.Verify(x => x.GenerateAsync(It.Is<string>(p => p.Contains("Çeviri doğrulamasında bulunan çelişki") && p.Contains("ecom.Product.Name")),
            It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        model.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CorrectFirstTranslationDoesNotTriggerAnExtraModelCall()
    {
        var model = Model(Correct);
        Assert.True((await Service(model).TranslateQuestionAsync(Question, Dictionary, "")).Success);
        model.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailedRepairRetainsTheOriginalContradictoryOutput()
    {
        var model = new Mock<ILlmClient>(MockBehavior.Strict);
        model.SetupGet(x => x.IsConfigured).Returns(true);
        model.SetupSequence(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Wrong).ThrowsAsync(new TimeoutException("Repair timed out."));
        var result = await Service(model).TranslateQuestionAsync(Question, Dictionary, "");
        Assert.False(result.Success);
        Assert.Equal(new[] { Wrong }, result.TranslationAttempts);
        Assert.Equal(Wrong, result.RawResponse);
        Assert.NotNull(result.SemanticValidationError);
    }

    [Fact]
    public async Task PersistentlyWrongTranslationCannotReachTheSqlWorkerAndIsDiagnosed()
    {
        await using var harness = new OwnedAnalysisEndpointHarness();
        harness.Config.ConfigJson = """
            {"technicalSchemaComplete":true,"tables":[{"name":"dbo.Orders"},{"name":"dbo.Customers"}],"questions":[],
             "columns":[{"table":"dbo.Customers","column":"Name","meaning":"Müşteri adı","role":"dimension"},
                        {"table":"dbo.Orders","column":"Id","role":"identifier"},
                        {"table":"dbo.Customers","column":"CustomerCode","meaning":"Müşteri kodu","role":"dimension"}]}
            """;
        await harness.Db.SaveChangesAsync();
        var model = Model("""{"target_table":"dbo.Customers","filters":{"CustomerCode":"123"}}""",
            """{"target_table":"dbo.Customers","filters":{"CustomerCode":"123"}}""");
        var worker = new Mock<IPyCaretQueryService>(MockBehavior.Strict);
        worker.SetupGet(x => x.IsConfigured).Returns(true);
        var result = await (Task<IResult>)typeof(AgentQueryEndpoints).GetMethod("SubmitQuery", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [new AgentQueryEndpoints.QueryRequest(harness.Connection.Id, "Ali 123 adlı müşteriyi bul"), harness.Db,
                Service(model), worker.Object, harness.Profiles, harness.Identity.Object, NullLogger<LlmAnalysisService>.Instance, CancellationToken.None])!;
        Assert.Equal(502, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Empty(harness.Db.QueryHistories);
        var diagnostic = await harness.Db.QueryDiagnostics.AsNoTracking().SingleAsync();
        Assert.Equal("entity_filter_mismatch", diagnostic.ErrorCode);
        Assert.Equal("translation", diagnostic.Stage);
        Assert.Equal(2, JsonNode.Parse(diagnostic.ContextJson)!["translationAttempts"]!.AsArray().Count);
        worker.VerifyGet(x => x.IsConfigured, Times.Once);
        worker.VerifyNoOtherCalls();
    }
}
