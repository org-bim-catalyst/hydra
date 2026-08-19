using AskLucy.Application.Abstractions;

namespace AskLucy.Infrastructure.Mcp;

/// <summary>
/// A fixed "system" identity for command handlers invoked from a Hangfire recurring job rather
/// than an HTTP request — there is no real caller to attribute the action to.
/// <c>HttpContextCurrentUserAccessor</c> (the app-wide DI registration) returns
/// <see langword="null"/> outside an HTTP request, and several MCP command handlers
/// (<c>TestMcpServerConnectionCommandHandler</c>, <c>RefreshMcpCapabilitiesCommandHandler</c>)
/// throw <see cref="UnauthorizedAccessException"/> on a null user id — this is supplied directly
/// to those handlers' constructors by the recurring jobs that need a scheduled/system-triggered
/// run, rather than changing the app-wide DI registration (which would affect every other,
/// genuinely user-triggered, caller of those same handlers).
/// </summary>
internal sealed class SystemCurrentUserAccessor : ICurrentUserAccessor
{
    public string? UserId => "system";

    public bool IsInRole(string role) => true;
}
