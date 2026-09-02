using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp.Commands.DeactivateMcpTool;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class DeactivateMcpToolCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpToolRepository _toolRepository = Substitute.For<IMcpToolRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpToolRegistry _mcpToolRegistry = Substitute.For<IMcpToolRegistry>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private DeactivateMcpToolCommandHandler CreateHandler() => new(_toolRepository, _auditLogRepository, _mcpToolRegistry, _unitOfWork, _currentUser);

    public DeactivateMcpToolCommandHandlerTests() => _currentUser.UserId.Returns(AdminId);

    [Fact]
    public async Task Handle_ShouldDeactivateTool_AndInvalidateRegistry()
    {
        var tool = McpTool.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "Searches things.", "{}", "{}", null, null, "[]", null, null);
        tool.Activate(AdminId, null, null);
        _toolRepository.GetByIdAsync(tool.Id, Arg.Any<CancellationToken>()).Returns(tool);
        var handler = CreateHandler();

        var result = await handler.Handle(new DeactivateMcpToolCommand(tool.McpServerId, tool.Id), CancellationToken.None);

        result.ActivationStatus.Should().Be(McpToolActivationStatus.Deactivated);
        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a!.Action == McpAuditAction.ToolDeactivated));
        await _mcpToolRegistry.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenToolNotFound()
    {
        _toolRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((McpTool?)null);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new DeactivateMcpToolCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenToolDoesNotBelongToTheRouteServer()
    {
        var tool = McpTool.CreateFromDiscovery(Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "Searches things.", "{}", "{}", null, null, "[]", null, null);
        tool.Activate(AdminId, null, null);
        _toolRepository.GetByIdAsync(tool.Id, Arg.Any<CancellationToken>()).Returns(tool);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new DeactivateMcpToolCommand(Guid.NewGuid(), tool.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
