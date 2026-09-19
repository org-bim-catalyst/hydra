using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Admin.Commands.IssueHangfireDashboardSession;

public sealed partial class IssueHangfireDashboardSessionCommandHandler(
    ITokenService tokenService,
    ICurrentUserAccessor currentUser,
    ILogger<IssueHangfireDashboardSessionCommandHandler> logger)
    : IRequestHandler<IssueHangfireDashboardSessionCommand, IssueHangfireDashboardSessionResult>
{
    public Task<IssueHangfireDashboardSessionResult> Handle(
        IssueHangfireDashboardSessionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        // The controller's [Authorize(Policy = "AdministratorOrSuperUser")] already guarantees
        // one of these two roles reached the handler at all — this only decides which one to
        // record. The role claim on the resulting token is informational/auditing only:
        // HangfireDashboardAuthorizationFilter re-checks role membership independently on every
        // request, it never trusts the token payload for that decision.
        var role = currentUser.IsInRole(PrivilegedRoleNames.Administrator)
            ? PrivilegedRoleNames.Administrator
            : PrivilegedRoleNames.SuperUser;

        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role),
            new Claim(HangfireDashboardSessionClaims.PurposeClaimType, HangfireDashboardSessionClaims.PurposeClaimValue),
        ];

        var token = tokenService.GenerateAccessToken(userId, claims, HangfireDashboardSessionClaims.Lifetime);

        LogSessionIssued(logger, userId, role);

        return Task.FromResult(new IssueHangfireDashboardSessionResult(token.AccessToken, HangfireDashboardSessionClaims.Lifetime));
    }

    [LoggerMessage(EventId = 5900, Level = LogLevel.Information, Message = "Hangfire dashboard session issued. UserId={UserId} Role={Role}")]
    private static partial void LogSessionIssued(ILogger logger, string userId, string role);
}
