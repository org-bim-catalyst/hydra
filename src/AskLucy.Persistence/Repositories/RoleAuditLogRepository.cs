using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;

namespace AskLucy.Persistence.Repositories;

/// <summary>Mirrors <c>McpAuditLogRepository</c>'s shape — <see cref="Add"/> only tracks the entity; the caller's own <see cref="AskLucyDbContext.SaveChangesAsync"/> commits it.</summary>
public sealed class RoleAuditLogRepository(AskLucyDbContext dbContext) : IRoleAuditLogRepository
{
    public void Add(RoleAuditLog entry) => dbContext.RoleAuditLogs.Add(entry);
}
