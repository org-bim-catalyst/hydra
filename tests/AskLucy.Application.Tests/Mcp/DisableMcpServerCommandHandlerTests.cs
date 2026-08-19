using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Commands.DisableMcpServer;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class DisableMcpServerCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpToolRegistry _mcpToolRegistry = Substitute.For<IMcpToolRegistry>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private DisableMcpServerCommandHandler CreateHandler() => new(_serverRepository, _auditLogRepository, _mcpToolRegistry, _unitOfWork, _currentUser);

    public DisableMcpServerCommandHandlerTests() => _currentUser.UserId.Returns(AdminId);

    [Fact]
    public async Task Handle_ShouldDisableServer_AndInvalidateRegistry()
    {
        var server = McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);
        server.Enable(AdminId);
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        var handler = CreateHandler();

        var result = await handler.Handle(new DisableMcpServerCommand(server.Id), CancellationToken.None);

        result.IsEnabled.Should().BeFalse();
        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a.Action == McpAuditAction.ServerDisabled));
        // FR-004/SC-008 — every tool from this server is immediately absent from ActiveTools.
        await _mcpToolRegistry.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }
}
