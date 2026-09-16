using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace AskLucy.Web.Auth;

/// <summary>
/// Writes an <see cref="RoleAuditAction.AuthorizationDenied"/> audit row (FR-024) when an
/// authenticated caller fails a <see cref="PermissionRequirement"/>, then delegates to the
/// framework's default handler for the actual 403 response — this never changes what the caller
/// receives, only what gets recorded.
/// </summary>
public sealed class PermissionDeniedAuditResultHandler(IRoleAuditLogRepository auditLog, IUnitOfWork unitOfWork) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var failedPermissionRequirements = authorizeResult.AuthorizationFailure?.FailedRequirements
                .OfType<PermissionRequirement>()
                .ToList();

            if (failedPermissionRequirements is { Count: > 0 })
            {
                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
                var requiredKeys = string.Join(", ", failedPermissionRequirements.SelectMany(r => r.AnyOf).Distinct());
                var detailsJson = $$"""{"requiredAnyOf":"{{requiredKeys}}","path":"{{context.Request.Path}}","method":"{{context.Request.Method}}"}""";

                auditLog.Add(RoleAuditLog.Record(RoleAuditAction.AuthorizationDenied, userId, detailsJson: detailsJson));
                await unitOfWork.SaveChangesAsync(context.RequestAborted);
            }
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
