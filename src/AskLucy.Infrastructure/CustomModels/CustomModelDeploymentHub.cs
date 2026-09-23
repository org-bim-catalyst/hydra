using System.Security.Claims;
using AskLucy.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.CustomModels;

/// <summary>
/// specs/072 contracts/custom-model-deployment-hub.md. Push-only. Unlike the per-user hubs
/// (<c>SiteAnalysisHub</c>), every deployment is visible to every admin who may view custom
/// models, so callers holding <c>admin.custom-models.view</c> join one shared group and anyone
/// else is refused (FR-026). The permission claims are the ones the claims transformation already
/// attached to the principal, exactly as <c>PermissionAuthorizationHandler</c> reads them.
/// </summary>
[Authorize]
public sealed partial class CustomModelDeploymentHub(ILogger<CustomModelDeploymentHub> logger) : Hub
{
    public const string ViewersGroup = "custom-model-viewers";

    public const string ViewPermission = "admin.custom-models.view";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is null || !user.FindAll(PermissionClaims.Type).Any(c => string.Equals(c.Value, ViewPermission, StringComparison.Ordinal)))
        {
            LogForbidden(logger, user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "(anonymous)");
            throw new HubException("Forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ViewersGroup);
        await base.OnConnectedAsync();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} was refused a custom-model deployments hub connection: missing admin.custom-models.view")]
    private static partial void LogForbidden(ILogger logger, string userId);
}
