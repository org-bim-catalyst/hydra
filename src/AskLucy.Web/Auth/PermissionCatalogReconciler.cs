using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Domain.Authorization;
using AskLucy.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Web.Auth;

/// <summary>
/// Startup sweep (research.md Decision 10): deletes any stored permission grant whose key is no
/// longer in <see cref="AdminPermissionCatalog"/> — a permission retired from the catalogue must
/// not linger, silently unenforced, on a role that still thinks it holds it. One
/// <see cref="RoleAuditAction.PermissionRetired"/> audit row per affected role.
/// </summary>
public sealed class PermissionCatalogReconciler(IServiceScopeFactory scopeFactory, ILogger<PermissionCatalogReconciler> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var auditLog = scope.ServiceProvider.GetRequiredService<IRoleAuditLogRepository>();

        var permissionClaims = await dbContext.RoleClaims
            .Where(c => c.ClaimType == PermissionClaims.Type)
            .ToListAsync(cancellationToken);

        var retiredByRole = permissionClaims
            .Where(c => c.ClaimValue is null || !AdminPermissionCatalog.TryGet(c.ClaimValue, out _))
            .GroupBy(c => c.RoleId)
            .ToList();

        if (retiredByRole.Count == 0)
        {
            return;
        }

        foreach (var group in retiredByRole)
        {
            dbContext.RoleClaims.RemoveRange(group);

            var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == group.Key, cancellationToken);
            var retiredKeysJson = string.Join(", ", group.Select(c => $"\"{c.ClaimValue}\""));
            auditLog.Add(RoleAuditLog.Record(
                RoleAuditAction.PermissionRetired, "system:reconciler", group.Key, role?.Name,
                detailsJson: $$"""{"retiredKeys":[{{retiredKeysJson}}]}"""));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

#pragma warning disable CA1848, CA1873 // one-time startup log, not a hot path
        logger.LogInformation(
            "Permission catalogue reconciler removed retired permission grants from {RoleCount} role(s).",
            retiredByRole.Count);
#pragma warning restore CA1848, CA1873
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
