using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Documents;
using AskLucy.Infrastructure.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.Documents;

/// <summary>
/// T064 — prompt construction/response parsing against a faked <see cref="IAIProvider"/> (no
/// live provider call). Lives in <c>Infrastructure.Tests</c>, not <c>Application.Tests</c> as
/// tasks.md literally states: <see cref="AiDocumentLanguageAndClassifier"/> is a concrete
/// Infrastructure class, and <c>AskLucy.Application.Tests</c> does not (and per constitution §3
/// should not) reference <c>AskLucy.Infrastructure</c>.
/// </summary>
public sealed class AiDocumentLanguageAndClassifierTests
{
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();

    private readonly AIProvider _provider;
    private readonly AIModel _model;

    public AiDocumentLanguageAndClassifierTests()
    {
        _provider = AIProvider.Create("openai", "OpenAI", "system");
        _provider.SetCredential("ciphertext", "system");
        _provider.Enable("system");

        _model = AIModel.Create(
            _provider.Id, "gpt-test", "GPT Test", 8000, 2000,
            new AIModelCapabilities(false, false, false, false, false, false, false, false, false),
            releaseDate: null, pricing: null, actor: "system");
        _provider.SetDefaultModel(_model.Id, "system");

        _providerRepository.GetByIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns(_provider);
        _providerRepository.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns([_provider]);
        _modelRepository.GetByIdAsync(_model.Id, Arg.Any<CancellationToken>()).Returns(_model);

        _providerResolver.Resolve(_provider.ProviderKey).Returns(_aiProvider);
    }

    private AiDocumentLanguageAndClassifier CreateSut() => new(
        new DefaultProviderResolver(_providerRepository, _modelRepository),
        _providerRepository,
        _modelRepository,
        _providerResolver,
        NullLogger<AiDocumentLanguageAndClassifier>.Instance);

    private void StubChatResponse(string content) =>
        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), _model.ModelKey, Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult(content, new ChatUsage(null, null, null, null, null)));

    [Fact]
    public async Task AnalyzeAsync_ShouldParsePrimaryAndSecondaryLanguagesAndCategory_FromValidJsonResponse()
    {
        StubChatResponse("""
            {"primaryLanguage":"en","secondaryLanguages":[{"code":"ar","confidence":0.3}],"category":"Legal","categoryConfidence":0.92}
            """);
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync("Some extracted document text.", ["Technical", "Legal", "Financial"], CancellationToken.None);

        result.Classification.CategoryName.Should().Be("Legal");
        result.Classification.ConfidenceScore.Should().Be(0.92m);
        result.Languages.Should().HaveCount(2);
        result.Languages[0].Role.Should().Be(DocumentLanguageRole.Primary);
        result.Languages[0].LanguageCode.Should().Be("en");
        result.Languages[1].Role.Should().Be(DocumentLanguageRole.Secondary);
        result.Languages[1].LanguageCode.Should().Be("ar");
        result.Languages[1].ConfidenceScore.Should().Be(0.3m);
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldTolerateProseWrappingTheJsonObject()
    {
        StubChatResponse("""
            Here is the analysis:
            ```json
            {"primaryLanguage":"en","secondaryLanguages":[],"category":"Technical","categoryConfidence":0.8}
            ```
            """);
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync("text", ["Technical"], CancellationToken.None);

        result.Classification.CategoryName.Should().Be("Technical");
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnLowConfidenceFallback_WhenResponseIsNotValidJson()
    {
        StubChatResponse("I'm not able to help with that.");
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync("text", ["Technical", "Legal"], CancellationToken.None);

        result.Classification.CategoryName.Should().Be("Technical");
        result.Classification.ConfidenceScore.Should().Be(0.1m);
        result.Languages.Should().ContainSingle(l => l.LanguageCode == "en" && l.ConfidenceScore == 0.1m);
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldFallBackToFirstCategory_WhenReturnedCategoryIsNotInTheProvidedList()
    {
        StubChatResponse("""
            {"primaryLanguage":"en","secondaryLanguages":[],"category":"NotARealCategory","categoryConfidence":0.5}
            """);
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync("text", ["Technical", "Legal"], CancellationToken.None);

        result.Classification.CategoryName.Should().Be("Technical");
    }
}
