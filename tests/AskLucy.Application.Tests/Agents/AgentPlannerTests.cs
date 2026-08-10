using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Domain.Agents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class AgentPlannerTests
{
    private readonly IAIProviderResolver _providerResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();

    public AgentPlannerTests()
    {
        _providerResolver.Resolve(Arg.Any<string>()).Returns(_aiProvider);
    }

    private static readonly AgentInstructions Instructions = new("You are a helpful assistant.", null, null, null, null, null, null);

    private sealed class FakeTool(string name) : IAgentTool
    {
        public string Name => name;
        public string Description => "A fake tool.";
        public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;
        public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];
        public string InputSchemaJson => "{}";
        public string OutputSchemaJson => "{}";
        public Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task CreatePlanAsync_ShouldReturnATrivialSingleStepPlan_WithoutCallingTheModel_WhenNoToolsAreConfigured()
    {
        var planner = new AgentPlanner(_providerResolver);

        var plan = await planner.CreatePlanAsync("Say hi.", Instructions, [], "openai", "gpt-5", true, CancellationToken.None);

        plan.Steps.Should().ContainSingle(s => s.StepType == AgentExecutionStepType.ModelReasoning);
        await _aiProvider.DidNotReceive().ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePlanAsync_ShouldParseAMultiStepToolSelectingPlan()
    {
        var tools = new IAgentTool[] { new FakeTool("KnowledgeSearchTool") };
        const string planJson = """{"goal":"Answer the question","steps":[{"description":"Search the knowledge base","toolName":"KnowledgeSearchTool","dependsOnStepIndex":null},{"description":"Summarize the findings","toolName":null,"dependsOnStepIndex":0}]}""";
        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult(planJson, new ChatUsage(10, 5, null, null, 50)));

        var planner = new AgentPlanner(_providerResolver);
        var plan = await planner.CreatePlanAsync("Answer the question.", Instructions, tools, "openai", "gpt-5", true, CancellationToken.None);

        plan.Steps.Should().HaveCount(2);
        plan.Steps[0].StepType.Should().Be(AgentExecutionStepType.ToolCall);
        plan.Steps[0].ToolName.Should().Be("KnowledgeSearchTool");
        plan.Steps[1].StepType.Should().Be(AgentExecutionStepType.ModelReasoning);
        plan.Steps[1].DependsOnStepIndex.Should().Be(0);
    }

    [Fact]
    public async Task CreatePlanAsync_ShouldRetryOnce_ThenSucceed_WhenTheFirstResponseIsInvalidJson()
    {
        var tools = new IAgentTool[] { new FakeTool("KnowledgeSearchTool") };
        const string validPlanJson = """{"goal":"Answer","steps":[{"description":"Search","toolName":"KnowledgeSearchTool"}]}""";
        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatCompletionResult("not json at all", new ChatUsage(5, 5, null, null, 10)),
                new ChatCompletionResult(validPlanJson, new ChatUsage(5, 5, null, null, 10)));

        var planner = new AgentPlanner(_providerResolver);
        var plan = await planner.CreatePlanAsync("Answer.", Instructions, tools, "openai", "gpt-5", true, CancellationToken.None);

        plan.Steps.Should().ContainSingle();
        await _aiProvider.Received(2).ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePlanAsync_ShouldThrow_WhenTheModelKeepsReturningInvalidJson()
    {
        var tools = new IAgentTool[] { new FakeTool("KnowledgeSearchTool") };
        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("still not json", new ChatUsage(5, 5, null, null, 10)));

        var planner = new AgentPlanner(_providerResolver);
        var act = () => planner.CreatePlanAsync("Answer.", Instructions, tools, "openai", "gpt-5", true, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreatePlanAsync_ShouldTreatAnUnrecognizedToolNameAsInvalid_AndRetry()
    {
        var tools = new IAgentTool[] { new FakeTool("KnowledgeSearchTool") };
        const string planWithUnknownTool = """{"goal":"Answer","steps":[{"description":"Search","toolName":"MadeUpTool"}]}""";
        const string validPlanJson = """{"goal":"Answer","steps":[{"description":"Search","toolName":"KnowledgeSearchTool"}]}""";
        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatCompletionResult(planWithUnknownTool, new ChatUsage(5, 5, null, null, 10)),
                new ChatCompletionResult(validPlanJson, new ChatUsage(5, 5, null, null, 10)));

        var planner = new AgentPlanner(_providerResolver);
        var plan = await planner.CreatePlanAsync("Answer.", Instructions, tools, "openai", "gpt-5", true, CancellationToken.None);

        plan.Steps.Should().ContainSingle(s => s.ToolName == "KnowledgeSearchTool");
    }
}
