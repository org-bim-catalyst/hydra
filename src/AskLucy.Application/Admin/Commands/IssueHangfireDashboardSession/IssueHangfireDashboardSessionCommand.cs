using MediatR;

namespace AskLucy.Application.Admin.Commands.IssueHangfireDashboardSession;

/// <summary>
/// Mints a short-lived, purpose-scoped access token for the calling Administrator/Super User to
/// reach the Hangfire dashboard (specs/060-hangfire-dashboard-access, research.md Decision 1/4).
/// The caller's id/role come from the authenticated principal via <c>ICurrentUserAccessor</c> in
/// the handler, not from request input — there is nothing here for a client to forge.
/// </summary>
public sealed record IssueHangfireDashboardSessionCommand : IRequest<IssueHangfireDashboardSessionResult>;

public sealed record IssueHangfireDashboardSessionResult(string AccessToken, TimeSpan Lifetime);
