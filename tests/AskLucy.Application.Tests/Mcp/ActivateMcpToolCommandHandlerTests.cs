using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Mcp;
using AskLucy.Application.Mcp.Commands.ActivateMcpTool;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class ActivateMcpToolCommandHandlerTests
{
    private const string AdminId = "admin-1";

    private readonly IMcpToolRepository _toolRepository = Substitute.For<IMcpToolRepository>();
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();
    private readonly IMcpToolRegistry _mcpToolRegistry = Substitute.For<IMcpToolRegistry>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private ActivateMcpToolCommandHandler CreateHandler() => new(_toolRepository, _auditLogRepository, _mcpToolRegistry, _unitOfWork, _currentUser);

    private static McpTool CreatePendingReviewTool(Guid? mcpServerId = null) => McpTool.CreateFromDiscovery(
        mcpServerId ?? Guid.NewGuid(), Guid.NewGuid(), "search", "Search", "Searches things.", "{}", "{}", null, null, "[]", null, null);

    public ActivateMcpToolCommandHandlerTests() => _currentUser.UserId.Returns(AdminId);

    [Fact]
    public async Task Handle_ShouldActivateTool_WhenPendingReview()
    {
        var tool = CreatePendingReviewTool();
        _toolRepository.GetByIdAsync(tool.Id, Arg.Any<CancellationToken>()).Returns(tool);
        var handler = CreateHandler();

        var result = await handler.Handle(new ActivateMcpToolCommand(tool.McpServerId, tool.Id, null, null), CancellationToken.None);

        result.ActivationStatus.Should().Be(McpToolActivationStatus.Active);
        _auditLogRepository.Received(1).Add(Arg.Is<McpAuditLog>(a => a.Action == McpAuditAction.ToolActivated));
        await _mcpToolRegistry.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReactivateTool_WhenPreviouslyDeactivated()
    {
        var tool = CreatePendingReviewTool();
        tool.Activate(AdminId, null, null);
        tool.Deactivate(AdminId);
        _toolRepository.GetByIdAsync(tool.Id, Arg.Any<CancellationToken>()).Returns(tool);
        var handler = CreateHandler();

        var result = await handler.Handle(new ActivateMcpToolCommand(tool.McpServerId, tool.Id, null, null), CancellationToken.None);

        result.ActivationStatus.Should().Be(McpToolActivationStatus.Active);
    }

    [Fact]
    public async Task Handle_ShouldApplyRiskAndPermissionOverrides_WhenProvided()
    {
        var tool = CreatePendingReviewTool();
        _toolRepository.GetByIdAsync(tool.Id, Arg.Any<CancellationToken>()).Returns(tool);
        var handler = CreateHandler();

        var result = await handler.Handle(new ActivateMcpToolCommand(tool.McpServerId, tool.Id, AgentToolRiskLevel.High, "[\"ReadExternalData\"]"), CancellationToken.None);

        result.EffectiveRiskLevel.Should().Be(AgentToolRiskLevelDto.High);
        result.RequiredPermissions.Should().ContainSingle().Which.Should().Be("ReadExternalData");
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenToolNotFound()
    {
        _toolRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((McpTool?)null);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ActivateMcpToolCommand(Guid.NewGuid(), Guid.NewGuid(), null, null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenToolDoesNotBelongToTheRouteServer()
    {
        var tool = CreatePendingReviewTool();
        _toolRepository.GetByIdAsync(tool.Id, Arg.Any<CancellationToken>()).Returns(tool);
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new ActivateMcpToolCommand(Guid.NewGuid(), tool.Id, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
