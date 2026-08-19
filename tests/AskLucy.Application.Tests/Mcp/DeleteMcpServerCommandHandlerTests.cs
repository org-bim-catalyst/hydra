using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.DeleteMcpServer;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

/// <summary>research.md Decision 15 — removal is strictly blocked while any agent still references this server's tools (spec.md FR-005, clarification).</summary>
public sealed class DeleteMcpServerCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private DeleteMcpServerCommandHandler CreateHandler() => new(_serverRepository, _auditLogRepository, _unitOfWork, _currentUser);

    public DeleteMcpServerCommandHandlerTests() => _currentUser.UserId.Returns(AdminId);

    private McpServer RegisterServer() => McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);

    [Fact]
    public async Task Handle_ShouldSoftDeleteServer_WhenNoReferences()
    {
        var server = RegisterServer();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.ListReferencingAgentToolsAsync(server.Id, Arg.Any<CancellationToken>()).Returns([]);
        var handler = CreateHandler();

        await handler.Handle(new DeleteMcpServerCommand(server.Id), CancellationToken.None);

        server.IsDeleted.Should().BeTrue();
        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a.Action == McpAuditAction.ServerRemoved));
    }

    [Fact]
    public async Task Handle_ShouldThrowMcpServerHasReferencesException_WhenAgentsStillReferenceIt()
    {
        var server = RegisterServer();
        var agentId = Guid.NewGuid();
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        _serverRepository.ListReferencingAgentToolsAsync(server.Id, Arg.Any<CancellationToken>())
            .Returns([(agentId, $"mcp:{server.Id}:search")]);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new DeleteMcpServerCommand(server.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<McpServerHasReferencesException>();
        exception.Which.ReferencingAgentTools.Should().ContainSingle(r => r.AgentId == agentId);
        server.IsDeleted.Should().BeFalse();
        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a.Action == McpAuditAction.ServerRemovalBlocked));
    }
}
