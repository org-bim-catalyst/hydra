using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Prompts;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

/// <summary>
/// T065b — resolves a saved Prompt's variables and calls through the existing
/// <see cref="IAIProvider"/> abstraction, never a raw provider call (research.md Decision 2).
/// </summary>
public sealed class PromptNodeExecutorTests
{
    private const string OwnerId = "user-1";

    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IAIProviderRepository _providerRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _modelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly IWorkflowExpressionEvaluator _expressionEvaluator = new WorkflowExpressionEvaluator();

    private PromptNodeExecutor CreateExecutor() =>
        new(_promptRepository, _providerRepository, _modelRepository, _providerResolver, _expressionEvaluator);

    private static (Prompt Prompt, PromptVersion Version) BuildPrompt()
    {
        var content = new PromptContentSnapshot(
            "You are helpful.", null, "Summarize {{document}}.", null, null, null, null, null, null, null, null, false);
        var variables = new List<PromptVariableDefinition>
        {
            new("document", null, PromptVariableType.String, true, null, null, null, 0),
        };
        return Prompt.Create(
            OwnerId, $"Prompt {Guid.NewGuid():N}", null, PromptType.Chat, null, null,
            PromptCapabilityRequirements.None, null, content, variables, OwnerId);
    }

    private static AIProvider BuildProvider() => AIProvider.Create("openai", "OpenAI", "system");

    private static AIModel BuildModel(Guid providerId) => AIModel.Create(
        providerId, "gpt-5", "GPT-5", 128000, 4096,
        new AIModelCapabilities(true, false, false, false, false, false, false, false, false), null, null, "system");

    private static WorkflowNode BuildNode(string configurationJson)
    {
        var workflow = Workflow.Create(OwnerId, "Test Workflow", null, WorkflowType.Manual, OwnerId);
        var spec = new WorkflowNodeSpec(
            "node", WorkflowNodeType.AiPrompt, "node", null, "{}", "{}", configurationJson, "[]", null, null, WorkflowNodeApprovalPolicy.NeverRequire, null, null, 0, 0);
        var version = workflow.Publish([spec], [], [], "{}", "{}", "{}", "{}", "{}", null, OwnerId);
        return version.Nodes.Single();
    }

    private static WorkflowNodeExecutionContext Context(WorkflowNode node) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), OwnerId, Guid.CreateVersion7(), Guid.CreateVersion7(), node);

    [Fact]
    public async Task ExecuteAsync_ShouldResolveVariablesAndGenerate_ThroughTheExistingAIProviderAbstraction()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);

        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        _providerResolver.Resolve(provider.ProviderKey).Returns(_aiProvider);
        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), model.ModelKey, Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("Generated text.", new ChatUsage(10, 5, null, null, null)));

        var configJson = JsonSerializer.Serialize(new
        {
            promptId = prompt.Id,
            providerId = provider.Id,
            modelId = model.Id,
            variableValues = new Dictionary<string, string> { ["document"] = "{{workflow.doc}}" },
        });

        var node = BuildNode(configJson);
        using var input = JsonDocument.Parse("""{"workflow.doc":"quarterly report"}""");

        var result = await CreateExecutor().ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("text").GetString().Should().Be("Generated text.");
        await _aiProvider.Received(1).ChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(m => m.Any(msg => msg.Content.Contains("quarterly report"))),
            model.ModelKey, Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenARequiredVariableIsMissing()
    {
        var (prompt, version) = BuildPrompt();
        var provider = BuildProvider();
        var model = BuildModel(provider.Id);

        _promptRepository.GetByIdForOwnerAsync(prompt.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(prompt);
        _promptRepository.GetVersionAsync(prompt.Id, version.VersionNumber, Arg.Any<CancellationToken>()).Returns(version);
        _providerRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _modelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);

        var configJson = JsonSerializer.Serialize(new { promptId = prompt.Id, providerId = provider.Id, modelId = model.Id });
        var node = BuildNode(configJson);
        using var input = JsonDocument.Parse("{}");

        var result = await CreateExecutor().ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("document");
        await _aiProvider.DidNotReceive().ChatAsync(
            Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenThePromptIsNotFoundOrNotOwnedByTheCaller()
    {
        var node = BuildNode(JsonSerializer.Serialize(new { promptId = Guid.NewGuid(), providerId = Guid.NewGuid(), modelId = Guid.NewGuid() }));
        using var input = JsonDocument.Parse("{}");

        var result = await CreateExecutor().ExecuteAsync(Context(node), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }
}
