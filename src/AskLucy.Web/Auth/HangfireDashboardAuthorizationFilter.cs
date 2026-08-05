using Hangfire.Dashboard;

namespace AskLucy.Web.Auth;

/// <summary>
/// Restricts the Hangfire dashboard (specs/015-document-intelligence-pipeline, research.md
/// Decision 2) to the Administrator/Super User roles, mirroring the "AdministratorOrSuperUser"
/// authorization policy already defined for admin endpoints in <c>Program.cs</c>. This host
/// authenticates every request via JWT Bearer only (no cookie/session auth) — a direct browser
/// navigation to <c>/hangfire</c> carries no Authorization header and will appear
/// unauthenticated, so this dashboard is reachable only via a client that attaches a valid
/// administrator bearer token (an API tool, or a future admin-SPA embed), not by typing the URL
/// into a browser address bar.
/// </summary>
public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var user = httpContext.User;

        return user.Identity?.IsAuthenticated == true
            && (user.IsInRole("Administrator") || user.IsInRole("Super User"));
    }
}
