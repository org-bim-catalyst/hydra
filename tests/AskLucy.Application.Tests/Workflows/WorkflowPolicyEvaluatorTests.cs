using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Domain.Workflows;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Workflows;

public sealed class WorkflowPolicyEvaluatorTests
{
    private readonly IWorkflowPolicyRepository _policyRepository = Substitute.For<IWorkflowPolicyRepository>();

    [Fact]
    public async Task FindMatchAsync_ShouldMatch_WhenConditionsJsonIsNull()
    {
        var policy = WorkflowPolicy.Create("Always allow", null, WorkflowNodeType.RagSearch, "KnowledgeSearchTool", conditionsJson: null, "admin-1");
        _policyRepository.ListEnabledForNodeAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", Arg.Any<CancellationToken>()).Returns([policy]);

        var evaluator = new WorkflowPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"query":"anything"}""", CancellationToken.None);

        match.Should().Be(policy);
    }

    [Fact]
    public async Task FindMatchAsync_ShouldReturnNull_WhenNoEnabledPolicyTargetsTheNode()
    {
        _policyRepository.ListEnabledForNodeAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", Arg.Any<CancellationToken>()).Returns([]);

        var evaluator = new WorkflowPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"query":"anything"}""", CancellationToken.None);

        match.Should().BeNull();
    }

    [Fact]
    public async Task FindMatchAsync_ShouldMatch_WhenEveryConditionEqualsTheActualInput()
    {
        var policy = WorkflowPolicy.Create("Public docs only", null, WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"visibility":"public"}""", "admin-1");
        _policyRepository.ListEnabledForNodeAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", Arg.Any<CancellationToken>()).Returns([policy]);

        var evaluator = new WorkflowPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"visibility":"public"}""", CancellationToken.None);

        match.Should().Be(policy);
    }

    [Fact]
    public async Task FindMatchAsync_ShouldNotMatch_WhenAConditionValueDiffersFromTheActualInput()
    {
        var policy = WorkflowPolicy.Create("Public docs only", null, WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"visibility":"public"}""", "admin-1");
        _policyRepository.ListEnabledForNodeAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", Arg.Any<CancellationToken>()).Returns([policy]);

        var evaluator = new WorkflowPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"visibility":"private"}""", CancellationToken.None);

        match.Should().BeNull();
    }

    [Fact]
    public async Task FindMatchAsync_ShouldNotMatch_WhenAConditionKeyIsMissingFromTheActualInput()
    {
        var policy = WorkflowPolicy.Create("Public docs only", null, WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"visibility":"public"}""", "admin-1");
        _policyRepository.ListEnabledForNodeAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", Arg.Any<CancellationToken>()).Returns([policy]);

        var evaluator = new WorkflowPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", "{}", CancellationToken.None);

        match.Should().BeNull();
    }

    [Fact]
    public async Task FindMatchAsync_ShouldReturnTheFirstMatchingPolicy_WhenSeveralAreEnabled()
    {
        var nonMatching = WorkflowPolicy.Create("Private allowed", null, WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"visibility":"private"}""", "admin-1");
        var matching = WorkflowPolicy.Create("Public allowed", null, WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"visibility":"public"}""", "admin-1");
        _policyRepository.ListEnabledForNodeAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", Arg.Any<CancellationToken>()).Returns([nonMatching, matching]);

        var evaluator = new WorkflowPolicyEvaluator(_policyRepository);
        var match = await evaluator.FindMatchAsync(WorkflowNodeType.RagSearch, "KnowledgeSearchTool", """{"visibility":"public"}""", CancellationToken.None);

        match.Should().Be(matching);
    }
}
