using System.Security.Claims;

namespace AskLucy.Web.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string FindFirstUserId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("No user id claim present.");
}
