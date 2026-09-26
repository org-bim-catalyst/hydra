using System.Security.Claims;

namespace AskLucy.Web.Auth;

/// <summary>
/// The partition key every per-user rate-limit policy in Program.cs uses.
/// </summary>
/// <remarks>
/// Access tokens carry the user id as <c>nameid</c> and no <c>name</c> claim, so
/// <c>User.Identity.Name</c> is always null. The policies used to key on it, which silently turned
/// every "per-user" limit into a per-IP one: everyone behind one NAT shared a bucket, and the first
/// caller's role fixed the tier for all of them. Anonymous requests still fall back to the client IP.
/// </remarks>
public static class RateLimitPartitions
{
    public static string UserOrClientKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
}
