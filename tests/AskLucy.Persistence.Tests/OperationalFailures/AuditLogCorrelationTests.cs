using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;
using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AskLucy.Persistence.Tests.OperationalFailures;

/// <summary>
/// specs/074 T030 (FR-006b). The correlation id is stamped by the audit interceptor, so every
/// existing role/MCP call site carries it without being edited — proven here against the real
/// column rather than a faked save.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class AuditLogCorrelationTests(PersistenceTestFixture fixture)
{
    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task AddedAuditRows_ShouldCarryTheCurrentCorrelationId()
    {
        var (roleLogId, mcpLogId) = await SaveBothAsync("abc");

        var (roleCorrelation, mcpCorrelation) = await ReadBothAsync(roleLogId, mcpLogId);
        roleCorrelation.Should().Be("abc");
        mcpCorrelation.Should().Be("abc");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task AddedAuditRows_ShouldStayNull_WhenThereIsNoCorrelationId()
    {
        var (roleLogId, mcpLogId) = await SaveBothAsync(null);

        var (roleCorrelation, mcpCorrelation) = await ReadBothAsync(roleLogId, mcpLogId);
        roleCorrelation.Should().BeNull();
        mcpCorrelation.Should().BeNull();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task AnOverlongInboundCorrelationId_ShouldBeClippedToTheColumn()
    {
        var (roleLogId, mcpLogId) = await SaveBothAsync(new string('x', 200));

        var (roleCorrelation, _) = await ReadBothAsync(roleLogId, mcpLogId);
        roleCorrelation.Should().HaveLength(64);
    }

    private async Task<(Guid RoleLogId, Guid McpLogId)> SaveBothAsync(string? correlationId)
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        var correlation = Substitute.For<ICorrelationIdAccessor>();
        correlation.Current.Returns(correlationId);

        var roleLog = RoleAuditLog.Record(RoleAuditAction.RoleCreated, "actor-074", targetRoleName: "Auditors");
        var mcpLog = McpAuditLog.Record(null, "actor-074", McpAuditAction.ServerRegistered, null, "{}");

        await using var dbContext = fixture.CreateAuditedDbContext(currentUser, correlation);
        dbContext.RoleAuditLogs.Add(roleLog);
        dbContext.McpAuditLogs.Add(mcpLog);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (roleLog.Id, mcpLog.Id);
    }

    private async Task<(string? Role, string? Mcp)> ReadBothAsync(Guid roleLogId, Guid mcpLogId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dbContext = fixture.CreateDbContext();
        var role = await dbContext.RoleAuditLogs.AsNoTracking().Where(l => l.Id == roleLogId).Select(l => l.CorrelationId).SingleAsync(ct);
        var mcp = await dbContext.McpAuditLogs.AsNoTracking().Where(l => l.Id == mcpLogId).Select(l => l.CorrelationId).SingleAsync(ct);
        return (role, mcp);
    }
}
