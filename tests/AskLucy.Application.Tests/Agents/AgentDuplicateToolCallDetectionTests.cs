using AskLucy.Application.Agents.Runtime;
using AskLucy.Domain.Agents;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class AgentDuplicateToolCallDetectionTests
{
    private static AgentToolCall CompletedCall(string toolName, string inputJson)
    {
        var call = AgentToolCall.Create(Guid.NewGuid(), toolName, AgentToolRiskLevel.Low, "[]", inputJson, wasApprovalRequired: false);
        call.Complete("{}");
        return call;
    }

    [Fact]
    public void IsDuplicate_ShouldBeTrue_ForAnExactRepeatOfACompletedCall()
    {
        var priorCalls = new[] { CompletedCall("KnowledgeSearchTool", """{"query":"onboarding"}""") };

        var result = new AgentDuplicateToolCallDetector().IsDuplicate(priorCalls, "KnowledgeSearchTool", """{"query":"onboarding"}""");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsDuplicate_ShouldBeFalse_ForTheSameToolWithDifferentInput()
    {
        var priorCalls = new[] { CompletedCall("KnowledgeSearchTool", """{"query":"onboarding"}""") };

        var result = new AgentDuplicateToolCallDetector().IsDuplicate(priorCalls, "KnowledgeSearchTool", """{"query":"pricing"}""");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsDuplicate_ShouldBeFalse_ForADifferentTool_WithTheSameInput()
    {
        var priorCalls = new[] { CompletedCall("KnowledgeSearchTool", """{"query":"onboarding"}""") };

        var result = new AgentDuplicateToolCallDetector().IsDuplicate(priorCalls, "DocumentSearchTool", """{"query":"onboarding"}""");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsDuplicate_ShouldBeFalse_WhenThePriorIdenticalCallFailed_SoARetryIsNotBlocked()
    {
        var failedCall = AgentToolCall.Create(Guid.NewGuid(), "KnowledgeSearchTool", AgentToolRiskLevel.Low, "[]", """{"query":"onboarding"}""", wasApprovalRequired: false);
        failedCall.Fail("Transient failure.");

        var result = new AgentDuplicateToolCallDetector().IsDuplicate([failedCall], "KnowledgeSearchTool", """{"query":"onboarding"}""");

        result.Should().BeFalse();
    }
}
