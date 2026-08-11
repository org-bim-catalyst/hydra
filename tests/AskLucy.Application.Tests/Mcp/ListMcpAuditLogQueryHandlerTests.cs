using AskLucy.Application.Abstractions;
using AskLucy.Application.Mcp.Queries.ListMcpAuditLog;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Mcp;

public sealed class ListMcpAuditLogQueryHandlerTests
{
    private readonly IMcpAuditLogRepository _auditLogRepository = Substitute.For<IMcpAuditLogRepository>();

    [Fact]
    public async Task Handle_ShouldReturnPagedAuditLog_WithNextCursor()
    {
        var serverId = Guid.NewGuid();
        var entry = McpAuditLog.Record(serverId, "admin-1", McpAuditAction.ServerRegistered, null, "{}");
        _auditLogRepository.ListByServerAsync(serverId, null, 20, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<McpAuditLog>)[entry], "next-cursor"));
        var handler = new ListMcpAuditLogQueryHandler(_auditLogRepository);

        var result = await handler.Handle(new ListMcpAuditLogQuery(serverId, null, 20), CancellationToken.None);

        result.Items.Should().ContainSingle(a => a.Action == McpAuditAction.ServerRegistered);
        result.NextCursor.Should().Be("next-cursor");
    }
}
