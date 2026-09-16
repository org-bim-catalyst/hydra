using AskLucy.Domain.Authorization;

namespace AskLucy.Application.Abstractions;

/// <summary>Append-only writer for <see cref="RoleAuditLog"/> (mirrors <c>IMcpAuditLogRepository</c>'s shape).</summary>
public interface IRoleAuditLogRepository
{
    void Add(RoleAuditLog entry);
}
