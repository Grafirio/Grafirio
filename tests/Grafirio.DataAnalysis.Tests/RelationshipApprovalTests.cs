using System.Text.Json;
using System.Text.Json.Nodes;
using Grafirio.DataAnalysis.Api.Data.Mongo;
using Grafirio.DataAnalysis.Api.Features.Agent;
using Grafirio.DataAnalysis.Api.Features.Profile;
using Grafirio.DataAnalysis.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grafirio.DataAnalysis.Tests;

public sealed class RelationshipApprovalTests
{
    private const string SourceTable = "dbo.Movements";
    private const string SourceColumn = "ReferanceId";
    private const string TargetTable = "dbo.References";
    private const string TargetColumn = "ReferenceId";
    private const string DeclaredSource = "declared";
    private const string InferredSource = "inferred";
    private const int ProposalLimit = 8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string DictionaryJson = """
        {
          "sector": "logistics",
          "tables": [{ "name": "dbo.Movements", "purpose": "Movement records" }],
          "columns": [
            { "table": "dbo.Movements", "column": "ReferanceId", "role": "identifier" },
            { "table": "dbo.Movements", "column": "ReferenceId", "role": "identifier" },
            { "table": "dbo.Movements", "column": "Amount", "role": "measure" },
            { "table": "dbo.References", "column": "ReferenceId", "role": "identifier" },
            { "table": "dbo.References", "column": "ExternalId", "role": "identifier" }
          ],
          "relationships": [],
          "rejectedRelationships": [],
          "profileStats": { "tableCount": 2, "columnCount": 5,
            "relationshipCount": 0, "inferredRelationshipCount": 0, "customMetric": 17 },
          "answers": [{ "table": "dbo.Movements", "answer": "Shipment movements" }],
          "codeValues": [{ "table": "dbo.Movements", "column": "Status",
            "values": ["OPEN"], "meanings": { "OPEN": "Active" } }],
          "customMetadata": { "nested": [true, null, { "preserve": "exactly" }] }
        }
        """;

    [Fact]
    public void ReadProposals_PreservesDistinctRealTypoColumnsWithoutMutatingDictionary()
    {
        var dictionary = CreateDictionary(CreateRelationship());
        var snapshot = dictionary.ToJsonString();
        var proposals = new[]
        {
            CreateProposal(),
            new RelationshipProposal(SourceTable, TargetColumn, TargetTable, TargetColumn)
        };
        var translation = Translation(proposals);

        var actual = RelationshipDictionary.ReadProposals(translation, snapshot);

        Assert.Equal(proposals, actual);
        Assert.NotEqual(actual[0].Key, actual[1].Key);
        Assert.Equal(snapshot, dictionary.ToJsonString());
        Assert.Equal(translation, Translation(proposals));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(snapshot), dictionary));
        Assert.True(dictionary["relationships"]![0]!["needsConfirmation"]!.GetValue<bool>());
    }

    [Fact]
    public void AppliedDecisions_UsesActiveDeclaredAndRejectedEdgesNotUnconfirmedInference()
    {
        var inferred = CreateRelationship();
        var dictionary = CreateDictionary(inferred).ToJsonString();
        Assert.Empty(RelationshipDictionary.AppliedDecisions(dictionary));

        var accepted = RelationshipDictionary.Apply(dictionary, CreateFact(true), CreateRelationship(DeclaredSource));
        Assert.True(RelationshipDictionary.AppliedDecisions(accepted)[CreateFact(true).Key]);

        var rejected = RelationshipDictionary.Apply(accepted, CreateFact(false), null);
        Assert.False(RelationshipDictionary.AppliedDecisions(rejected)[CreateFact(false).Key]);
        Assert.Empty(RelationshipDictionary.AppliedDecisions("{}"));
    }

    [Fact]
    public void ReadProposals_DeduplicatesCaseInsensitiveKeysWithoutRewritingNames()
    {
        var proposal = CreateProposal();
        var uppercase = new RelationshipProposal(
            SourceTable.ToUpperInvariant(), SourceColumn.ToUpperInvariant(),
            TargetTable.ToUpperInvariant(), TargetColumn.ToUpperInvariant());

        var actual = RelationshipDictionary.ReadProposals(
            Translation(proposal, uppercase), DictionaryJson);

        Assert.Equal(proposal, Assert.Single(actual));
    }

    [Theory]
    [InlineData("dbo.Missing", SourceColumn, TargetTable, TargetColumn)]
    [InlineData(SourceTable, "MissingId", TargetTable, TargetColumn)]
    [InlineData(SourceTable, SourceColumn, "dbo.Missing", TargetColumn)]
    [InlineData(SourceTable, SourceColumn, TargetTable, "MissingId")]
    [InlineData(SourceTable, "ExternalId", TargetTable, TargetColumn)]
    [InlineData(SourceTable, SourceColumn, TargetTable, SourceColumn)]
    public void ReadProposals_RejectsColumnsMissingFromTheirSpecificTable(
        string fromTable, string fromColumn, string toTable, string toColumn)
    {
        var translation = Translation(new RelationshipProposal(fromTable, fromColumn, toTable, toColumn));

        Assert.Throws<ArgumentException>(() =>
            RelationshipDictionary.ReadProposals(translation, DictionaryJson));
    }

    [Fact]
    public void ReadProposals_AllowsExactlyEightDistinctProposals()
    {
        var (dictionary, proposals) = CreateProposalBatch(ProposalLimit);

        var actual = RelationshipDictionary.ReadProposals(Translation(proposals), dictionary);

        Assert.Equal(ProposalLimit, actual.Count);
        Assert.Equal(proposals, actual);
    }

    [Fact]
    public void ReadProposals_RejectsNinthProposal()
    {
        var (dictionary, proposals) = CreateProposalBatch(ProposalLimit + 1);

        Assert.Throws<ArgumentException>(() =>
            RelationshipDictionary.ReadProposals(Translation(proposals), dictionary));
    }

    [Fact]
    public void ReadProposals_EnforcesLimitBeforeDeduplication()
    {
        var proposals = Enumerable.Repeat(CreateProposal(), ProposalLimit + 1).ToArray();

        Assert.Throws<ArgumentException>(() =>
            RelationshipDictionary.ReadProposals(Translation(proposals), DictionaryJson));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"relationship_proposals\":[]}")]
    public void ReadProposals_ReturnsEmptyWhenNoProposalExists(string translation)
    {
        Assert.Empty(RelationshipDictionary.ReadProposals(translation, DictionaryJson));
    }

    public static IEnumerable<object[]> MalformedProposalFields()
    {
        string[] fields = ["fromTable", "fromColumn", "toTable", "toColumn"];
        string[] values = ["null", "\"\"", "\"   \"", "42", "true", "{}", "[]"];
        foreach (var field in fields)
        {
            var missing = JsonSerializer.SerializeToNode(CreateProposal(), JsonOptions)!.AsObject();
            missing.Remove(field);
            yield return [field, "missing", missing.ToJsonString()];
            foreach (var value in values)
            {
                var malformed = JsonSerializer.SerializeToNode(CreateProposal(), JsonOptions)!.AsObject();
                malformed[field] = JsonNode.Parse(value);
                yield return [field, value, malformed.ToJsonString()];
            }
        }
    }

    [Theory]
    [MemberData(nameof(MalformedProposalFields))]
    public void ReadProposals_RejectsMalformedFieldsWithControlledValidationError(
        string field, string invalidValue, string proposalJson)
    {
        var translation = $$"""{"relationship_proposals":[{{proposalJson}}]}""";

        var exception = Record.Exception(() =>
            RelationshipDictionary.ReadProposals(translation, DictionaryJson));

        Assert.True(exception is ArgumentException,
            $"Field {field} with value {invalidValue} must produce ArgumentException, not {exception?.GetType().Name ?? "success"}.");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("\"invalid\"")]
    [InlineData("[]")]
    public void ReadProposals_RejectsMalformedArrayEntries(string entryJson)
    {
        var translation = $$"""{"relationship_proposals":[{{entryJson}}]}""";

        Assert.Throws<ArgumentException>(() =>
            RelationshipDictionary.ReadProposals(translation, DictionaryJson));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("\"invalid\"")]
    public void ReadProposals_RejectsMalformedProposalContainerInsteadOfIgnoringIt(string containerJson)
    {
        var translation = $$"""{"relationship_proposals":{{containerJson}}}""";

        Assert.Throws<ArgumentException>(() =>
            RelationshipDictionary.ReadProposals(translation, DictionaryJson));
    }

    [Fact]
    public void ReadProposals_DoesNotPartiallyReturnWhenLaterProposalIsInvalid()
    {
        var valid = CreateProposal();
        var invalid = valid with { ToColumn = "MissingId" };

        Assert.Throws<ArgumentException>(() =>
            RelationshipDictionary.ReadProposals(Translation(valid, invalid), DictionaryJson));
        Assert.Equal(valid, Assert.Single(RelationshipDictionary.ReadProposals(
            Translation(valid), DictionaryJson)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_ReplacesOrRemovesOnlyExactNormalizedSingleColumnKey(bool accepted)
    {
        var exact = CreateRelationship();
        var normalizedDuplicate = CreateRelationship();
        normalizedDuplicate.FromTable = "[DBO].[MOVEMENTS]";
        normalizedDuplicate.FromColumns = ["[REFERANCEID]"];
        var differentSourceColumn = CreateRelationship();
        differentSourceColumn.FromColumns = [TargetColumn];
        var differentTargetColumn = CreateRelationship();
        differentTargetColumn.ToColumns = ["ExternalId"];
        var differentSourceTable = CreateRelationship();
        differentSourceTable.FromTable = "archive.Movements";
        var differentTargetTable = CreateRelationship();
        differentTargetTable.ToTable = "archive.References";
        var reverse = CreateRelationship();
        reverse.FromTable = TargetTable;
        reverse.FromColumns = [TargetColumn];
        reverse.ToTable = SourceTable;
        reverse.ToColumns = [SourceColumn];
        var composite = CreateRelationship();
        composite.FromColumns = [SourceColumn, "TenantId"];
        composite.ToColumns = [TargetColumn, "TenantId"];
        RelationshipProfile[] unrelated = [differentSourceColumn, differentTargetColumn,
            differentSourceTable, differentTargetTable, reverse, composite];
        var dictionary = CreateDictionary([exact, normalizedDuplicate, .. unrelated]);
        var original = dictionary.ToJsonString();
        var replacement = CreateRelationship(DeclaredSource);

        var updated = JsonNode.Parse(RelationshipDictionary.Apply(
            original, CreateFact(accepted), accepted ? replacement : null))!.AsObject();
        var edges = updated["relationships"]!.AsArray();

        Assert.Equal(unrelated.Length + (accepted ? 1 : 0), edges.Count);
        for (var index = 0; index < unrelated.Length; index++)
            Assert.True(JsonNode.DeepEquals(
                JsonSerializer.SerializeToNode(unrelated[index], JsonOptions), edges[index]));
        if (accepted)
        {
            var applied = edges.Last()!.Deserialize<RelationshipProfile>(JsonOptions)!;
            Assert.False(applied.NeedsConfirmation);
            Assert.Equal(DeclaredSource, applied.Source);
            Assert.Equal(SourceColumn, Assert.Single(applied.FromColumns));
            Assert.Equal(TargetColumn, Assert.Single(applied.ToColumns));
        }
        Assert.Equal(edges.Count, updated["profileStats"]!["relationshipCount"]!.GetValue<int>());
        Assert.Equal(unrelated.Length, updated["profileStats"]!["inferredRelationshipCount"]!.GetValue<int>());
        AssertUnrelatedDictionaryPreserved(dictionary, updated);
        Assert.Equal(original, dictionary.ToJsonString());
    }

    [Fact]
    public void Apply_RejectionIsIdempotentAndReacceptanceClearsOnlyMatchingRejection()
    {
        var unrelated = new RelationshipProposal(SourceTable, TargetColumn, TargetTable, "ExternalId");
        var dictionary = CreateDictionary(CreateRelationship());
        dictionary[RelationshipDictionary.RejectedProperty] = JsonSerializer.SerializeToNode(
            new[] { unrelated }, JsonOptions);

        var rejectedOnce = RelationshipDictionary.Apply(dictionary.ToJsonString(), CreateFact(false), null);
        var rejectedTwice = RelationshipDictionary.Apply(rejectedOnce, CreateFact(false), null);
        var rejectedRoot = JsonNode.Parse(rejectedTwice)!.AsObject();
        var rejections = rejectedRoot[RelationshipDictionary.RejectedProperty]!
            .Deserialize<List<RelationshipProposal>>(JsonOptions)!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(rejectedOnce), rejectedRoot));
        Assert.Equal(new[] { unrelated, CreateProposal() }, rejections);
        Assert.Empty(rejectedRoot["relationships"]!.AsArray());
        Assert.Throws<ArgumentException>(() => RelationshipDictionary.ReadProposals(
            Translation(CreateProposal() with { FromColumn = SourceColumn.ToUpperInvariant() }), rejectedTwice));
        AssertUnrelatedDictionaryPreserved(dictionary, rejectedRoot);

        var acceptedOnce = RelationshipDictionary.Apply(rejectedTwice, CreateFact(true), CreateRelationship(DeclaredSource));
        var acceptedTwice = RelationshipDictionary.Apply(acceptedOnce, CreateFact(true), CreateRelationship(DeclaredSource));
        var acceptedRoot = JsonNode.Parse(acceptedTwice)!.AsObject();

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(acceptedOnce), acceptedRoot));
        Assert.Equal(unrelated, Assert.Single(acceptedRoot[RelationshipDictionary.RejectedProperty]!
            .Deserialize<List<RelationshipProposal>>(JsonOptions)!));
        Assert.Single(acceptedRoot["relationships"]!.AsArray());
        Assert.Equal(1, acceptedRoot["profileStats"]!["relationshipCount"]!.GetValue<int>());
        Assert.Equal(0, acceptedRoot["profileStats"]!["inferredRelationshipCount"]!.GetValue<int>());
        Assert.Equal(CreateProposal(), Assert.Single(RelationshipDictionary.ReadProposals(
            Translation(CreateProposal()), acceptedTwice)));
        AssertUnrelatedDictionaryPreserved(dictionary, acceptedRoot);
    }

    [Fact]
    public void Apply_CreatesMissingCollectionsWithoutInventingProfileStats()
    {
        const string minimalDictionary = """{"customMetadata":{"preserve":true}}""";

        var updated = JsonNode.Parse(RelationshipDictionary.Apply(
            minimalDictionary, CreateFact(true), CreateRelationship(DeclaredSource)))!.AsObject();

        Assert.Single(updated["relationships"]!.AsArray());
        Assert.Empty(updated[RelationshipDictionary.RejectedProperty]!.AsArray());
        Assert.False(updated.ContainsKey("profileStats"));
        Assert.True(updated["customMetadata"]!["preserve"]!.GetValue<bool>());
    }

    [Fact]
    public void Apply_AcceptanceRequiresValidatedRelationship()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RelationshipDictionary.Apply(DictionaryJson, CreateFact(true), null));
    }

    [Fact]
    public async Task TranslateQuestionAsync_PromptIncludesStructuredProposalsAndPreviousConversation()
    {
        const string question = "evet, bu eşleşmeyle önceki soruyu cevapla";
        const string summary = "Shipment movements and reference records.";
        var conversation = ConversationContext.Render([
            new ConversationContext.Turn("Referansa göre toplam tutar nedir?",
                ConversationContext.ClarificationStatus, "Bu iki alanı eşleştirelim mi?", "{}", null)
        ]);
        var client = new CapturingLlmClient();
        var service = new LlmAnalysisService(client, NullLogger<LlmAnalysisService>.Instance);

        var result = await service.TranslateQuestionAsync(question, DictionaryJson, summary, conversation);

        Assert.True(result.Success);
        var prompt = Assert.IsType<string>(client.Prompt);
        var normalizedPrompt = NormalizeWhitespace(prompt);
        Assert.Contains(DictionaryJson, prompt);
        Assert.Contains(summary, prompt);
        Assert.Contains(question, prompt);
        Assert.Contains(conversation.TrimEnd(), prompt);
        Assert.True(prompt.IndexOf(conversation.TrimEnd(), StringComparison.Ordinal)
            < prompt.IndexOf("## Kullanıcının sorusu", StringComparison.Ordinal));
        Assert.Contains("\"relationship_proposals\": [{ \"fromTable\":", normalizedPrompt);
        Assert.Contains("\"fromColumn\": \"ReferanceId\"", normalizedPrompt);
        Assert.Contains("\"toTable\": \"dbo.Referanslar\"", normalizedPrompt);
        Assert.Contains("\"toColumn\": \"ReferenceId\"", normalizedPrompt);
        Assert.Contains("En fazla 8 öneri", normalizedPrompt);
        Assert.Contains("`rejectedRelationships` içindeki eşleşmeleri tekrar önerme", normalizedPrompt);
        Assert.Contains("Öneri varken `target_table`'ı boş bırak", normalizedPrompt);
        Assert.Contains("yapılandırılmış öneri zorunludur", normalizedPrompt);
        Assert.Contains("Sohbetteki \"evet\" sözcüğü tek başına yeni ilişki yaratmaz", normalizedPrompt);
        Assert.Contains("Onay uygulanmışsa güncel `relationships` listesini kullan", normalizedPrompt);
        Assert.Contains("önceki turdaki öneriyi KOPYALAMA", normalizedPrompt);
        Assert.DoesNotMatch(@"(?i)(kullanıcıdan|kullanıcıya)\s+onay\s+(isteme|sorma)", normalizedPrompt);
        Assert.DoesNotMatch(@"(?i)onay\s+(istemek|sormak)\s+(yasak|yok)", normalizedPrompt);
    }

    [Fact]
    public async Task TranslateQuestionAsync_PromptAllowsAggregateAndHavingWithoutGrouping()
    {
        var client = new CapturingLlmClient();
        var service = new LlmAnalysisService(client, NullLogger<LlmAnalysisService>.Instance);

        var result = await service.TranslateQuestionAsync(
            "Genel toplam bir milyonu geçiyorsa göster", DictionaryJson, "Movement amounts.");

        Assert.True(result.Success);
        var prompt = NormalizeWhitespace(Assert.IsType<string>(client.Prompt));
        Assert.Contains("Genel toplam veya tek toplamın HAVING koşulu isteniyorsa `group_by: []` kullan", prompt);
        Assert.Contains("sahte kırılım ekleme", prompt);
        Assert.Contains("Kırılım isteniyorsa `group_by` ilgili `dimension` kolonlarını içermeli", prompt);
        Assert.DoesNotContain("aggregation için group_by zorunlu", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static RelationshipProposal CreateProposal() =>
        new(SourceTable, SourceColumn, TargetTable, TargetColumn);

    private static LearnedFact CreateFact(bool accepted) =>
        LearnedFact.ForRelationship(SourceTable, SourceColumn, TargetTable, TargetColumn, accepted);

    private static RelationshipProfile CreateRelationship(string source = InferredSource) => new()
    {
        FromTable = SourceTable,
        FromColumns = [SourceColumn],
        ToTable = TargetTable,
        ToColumns = [TargetColumn],
        Source = source,
        NeedsConfirmation = true,
        LabelColumn = "Name",
        ValueOverlap = 1,
        Note = "Preserve measured relationship details."
    };

    private static JsonObject CreateDictionary(params RelationshipProfile[] relationships)
    {
        var dictionary = JsonNode.Parse(DictionaryJson)!.AsObject();
        dictionary["relationships"] = JsonSerializer.SerializeToNode(relationships, JsonOptions);
        dictionary["profileStats"]!["relationshipCount"] = relationships.Length;
        dictionary["profileStats"]!["inferredRelationshipCount"] =
            relationships.Count(relationship => relationship.Source == InferredSource);
        return dictionary;
    }

    private static string Translation(params RelationshipProposal[] proposals) =>
        new JsonObject
        {
            [RelationshipDictionary.ProposalsProperty] = JsonSerializer.SerializeToNode(proposals, JsonOptions)
        }.ToJsonString();

    private static (string Dictionary, RelationshipProposal[] Proposals) CreateProposalBatch(int count)
    {
        var dictionary = CreateDictionary();
        var columns = dictionary["columns"]!.AsArray();
        var proposals = Enumerable.Range(1, count).Select(index =>
        {
            var column = $"ReferenceId{index}";
            columns.Add(new JsonObject { ["table"] = SourceTable, ["column"] = column });
            return new RelationshipProposal(SourceTable, column, TargetTable, TargetColumn);
        }).ToArray();
        return (dictionary.ToJsonString(), proposals);
    }

    private static void AssertUnrelatedDictionaryPreserved(JsonObject before, JsonObject after)
    {
        var expected = before.DeepClone().AsObject();
        var actual = after.DeepClone().AsObject();
        foreach (var root in new[] { expected, actual })
        {
            root.Remove("relationships");
            root.Remove(RelationshipDictionary.RejectedProperty);
            root["profileStats"]!.AsObject().Remove("relationshipCount");
            root["profileStats"]!.AsObject().Remove("inferredRelationshipCount");
        }
        Assert.True(JsonNode.DeepEquals(expected, actual), "Unrelated dictionary content must remain unchanged.");
    }

    private static string NormalizeWhitespace(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class CapturingLlmClient : ILlmClient
    {
        public bool IsConfigured => true;
        public string? Prompt { get; private set; }

        public Task<string> GenerateAsync(
            string prompt, double temperature = 0.2, int maxTokens = 2048,
            CancellationToken cancellationToken = default)
        {
            Prompt = prompt;
            return Task.FromResult("""{"analysis_type":"aggregation","target_table":"dbo.Movements"}""");
        }
    }
}