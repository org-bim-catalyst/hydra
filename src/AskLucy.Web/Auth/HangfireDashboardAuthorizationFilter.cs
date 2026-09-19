using Hangfire.Dashboard;

namespace AskLucy.Web.Auth;

/// <summary>
/// Restricts the Hangfire dashboard (specs/015-document-intelligence-pipeline, research.md
/// Decision 2) to the Administrator/Super User roles, mirroring the "AdministratorOrSuperUser"
/// authorization policy already defined for admin endpoints in <c>Program.cs</c>. A direct
/// browser navigation to <c>/hangfire</c> carries no Authorization header, but (as of
/// specs/060-hangfire-dashboard-access) it can carry the short-lived, path-scoped
/// <see cref="HangfireDashboardCookie"/>, which <c>Program.cs</c>'s <c>OnMessageReceived</c>
/// reads into the same JWT Bearer scheme this filter's role checks rely on — so this stays a
/// pure role check, unaware of which credential source authenticated the request.
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
