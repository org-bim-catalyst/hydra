using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Commands.EnableMcpServer;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class EnableMcpServerCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpServerRepository _serverRepository = Substitute.For<IMcpServerRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private EnableMcpServerCommandHandler CreateHandler() => new(_serverRepository, _auditLogRepository, _unitOfWork, _currentUser);

    public EnableMcpServerCommandHandlerTests() => _currentUser.UserId.Returns(AdminId);

    [Fact]
    public async Task Handle_ShouldEnableServer_AndRecordAudit()
    {
        var server = McpServer.Register("Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);
        _serverRepository.GetByIdAsync(server.Id, Arg.Any<CancellationToken>()).Returns(server);
        var handler = CreateHandler();

        var result = await handler.Handle(new EnableMcpServerCommand(server.Id), CancellationToken.None);

        result.IsEnabled.Should().BeTrue();
        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a.Action == McpAuditAction.ServerEnabled));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenServerNotFound()
    {
        _serverRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((McpServer?)null);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new EnableMcpServerCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
