using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.DuplicateMcpPrompt;
using AskLucy.Domain.Mcp;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>spec.md FR-041-FR-044, research.md Decision 16 — duplicating an MCP prompt creates an independent, user-owned native `Prompt`; the source `McpPrompt` is a read-only mirror, unaffected and never directly editable.</summary>
public sealed class DuplicateMcpPromptCommandHandlerTests
{
    private const string OwnerId = "user-1";

    private readonly IMcpPromptRepository _mcpPromptRepository = Substitute.For<IMcpPromptRepository>();
    private readonly IPromptRepository _promptRepository = Substitute.For<IPromptRepository>();
    private readonly IPromptAuditLogRepository _auditLogRepository = Substitute.For<IPromptAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private DuplicateMcpPromptCommandHandler CreateHandler() => new(_mcpPromptRepository, _promptRepository, _auditLogRepository, _unitOfWork, _currentUser);

    public DuplicateMcpPromptCommandHandlerTests() => _currentUser.UserId.Returns(OwnerId);

    private static McpPrompt CreateSourcePrompt(string content = "You are a helpful research assistant.") =>
        McpPrompt.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "researchAssistant", "A research assistant prompt.", content);

    [Fact]
    public async Task Handle_ShouldCreateAnIndependentNativePrompt_SeededFromTheMcpPromptsContentTemplate()
    {
        var source = CreateSourcePrompt();
        _mcpPromptRepository.GetByNamespacedNameAsync(source.NamespacedName, Arg.Any<CancellationToken>()).Returns(source);
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Prompt?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new DuplicateMcpPromptCommand(source.NamespacedName), CancellationToken.None);

        result.UserInstructions.Should().Be("You are a helpful research assistant.");
        _promptRepository.Received(1).Add(Arg.Is<Prompt>(p => p!.OwnerId == OwnerId && p.Name == source.Name));
    }

    [Fact]
    public async Task Handle_ShouldResolveANonConflictingName_WhenTheOwnerAlreadyHasAPromptWithThatName()
    {
        var source = CreateSourcePrompt();
        _mcpPromptRepository.GetByNamespacedNameAsync(source.NamespacedName, Arg.Any<CancellationToken>()).Returns(source);
        var existing = Prompt.Create(
            OwnerId, source.Name, null, PromptType.Chat, null, null, PromptCapabilityRequirements.None, null,
            new PromptContentSnapshot(null, null, "existing", null, null, null, null, null, null, null, null, false), [], OwnerId).Prompt;
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, source.Name, Arg.Any<CancellationToken>()).Returns(existing);
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, $"{source.Name} 2", Arg.Any<CancellationToken>()).Returns((Prompt?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new DuplicateMcpPromptCommand(source.NamespacedName), CancellationToken.None);

        result.Name.Should().Be($"{source.Name} 2");
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenTheSourceMcpPromptDoesNotExist()
    {
        _mcpPromptRepository.GetByNamespacedNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((McpPrompt?)null);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new DuplicateMcpPromptCommand("mcp:nonexistent:prompt"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldNeverMutateTheSourceMcpPrompt()
    {
        var source = CreateSourcePrompt();
        var originalContent = source.ContentTemplate;
        _mcpPromptRepository.GetByNamespacedNameAsync(source.NamespacedName, Arg.Any<CancellationToken>()).Returns(source);
        _promptRepository.GetByOwnerAndNameAsync(OwnerId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Prompt?)null);
        var handler = CreateHandler();

        await handler.Handle(new DuplicateMcpPromptCommand(source.NamespacedName), CancellationToken.None);

        // McpPrompt has no direct-edit method at all (only RefreshFromSnapshot, called by capability
        // discovery) — the type system itself guarantees this handler cannot have mutated it.
        source.ContentTemplate.Should().Be(originalContent);
    }
}
