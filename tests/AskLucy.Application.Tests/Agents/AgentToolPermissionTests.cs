using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Documents;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>
/// spec.md FR-020-FR-023: every tool declares its required permissions/risk level up front
/// (contracts/agent-tool-contract.md's table), and every tool's own scoped repository/guard call
/// is what actually enforces "never exceed the executing user's own permissions" — there is no
/// separate abstract permission registry to bypass (contracts/agent-tool-contract.md's Runtime
/// contract item 2).
/// </summary>
public sealed class AgentToolPermissionTests
{
    [Theory]
    [InlineData("ConversationTool", AgentToolRiskLevel.Low)]
    [InlineData("KnowledgeSearchTool", AgentToolRiskLevel.Low)]
    [InlineData("DocumentSearchTool", AgentToolRiskLevel.Low)]
    [InlineData("MemorySearchTool", AgentToolRiskLevel.Low)]
    [InlineData("MemoryWriteTool", AgentToolRiskLevel.Medium)]
    [InlineData("PromptExecutionTool", AgentToolRiskLevel.Low)]
    [InlineData("FileReadTool", AgentToolRiskLevel.Low)]
    [InlineData("FileMetadataTool", AgentToolRiskLevel.Low)]
    public void EveryBuiltInTool_ShouldDeclareTheRiskLevelDocumentedInTheContract(string toolName, AgentToolRiskLevel expectedRiskLevel)
    {
        var tool = CreateTool(toolName);

        tool.Name.Should().Be(toolName);
        tool.RiskLevel.Should().Be(expectedRiskLevel);
    }

    [Fact]
    public void NoBuiltInTool_ShouldDeclareAHighOrCriticalRiskLevel_ThisRelease()
    {
        var toolNames = new[]
        {
            "ConversationTool", "KnowledgeSearchTool", "DocumentSearchTool", "MemorySearchTool",
            "MemoryWriteTool", "PromptExecutionTool", "FileReadTool", "FileMetadataTool",
        };

        foreach (var name in toolNames)
        {
            CreateTool(name).RiskLevel.Should().BeOneOf(AgentToolRiskLevel.Low, AgentToolRiskLevel.Medium);
        }
    }

    [Fact]
    public async Task FileReadTool_ShouldNeverReadADocumentTheCallerDoesNotOwn()
    {
        var documentRepository = Substitute.For<IDocumentRepository>();
        var otherUsersDocument = Document.Create(Guid.NewGuid(), "someone-else", "secret.pdf", DocumentFileType.Pdf, 100, Guid.NewGuid(), "actor");
        documentRepository.GetByIdAsync(otherUsersDocument.Id, Arg.Any<CancellationToken>()).Returns(otherUsersDocument);

        var tool = new FileReadTool(documentRepository, Substitute.For<IFileStorage>());
        using var input = JsonDocument.Parse($$"""{"documentId":"{{otherUsersDocument.Id}}"}""");

        var act = () => tool.ExecuteAsync(new AgentToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), null), input, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private static IAgentTool CreateTool(string toolName) => toolName switch
    {
        "ConversationTool" => new ConversationTool(Substitute.For<IMessageRepository>()),
        "KnowledgeSearchTool" => new KnowledgeSearchTool(Substitute.For<IRagService>(), Substitute.For<IKnowledgeBaseRepository>()),
        "DocumentSearchTool" => new DocumentSearchTool(Substitute.For<IDocumentRepository>()),
        "MemorySearchTool" => new MemorySearchTool(Substitute.For<IMemoryService>()),
        "MemoryWriteTool" => new MemoryWriteTool(Substitute.For<ISender>()),
        "PromptExecutionTool" => new PromptExecutionTool(Substitute.For<IPromptRepository>()),
        "FileReadTool" => new FileReadTool(Substitute.For<IDocumentRepository>(), Substitute.For<IFileStorage>()),
        "FileMetadataTool" => new FileMetadataTool(Substitute.For<IDocumentRepository>()),
        _ => throw new ArgumentOutOfRangeException(nameof(toolName)),
    };
}
