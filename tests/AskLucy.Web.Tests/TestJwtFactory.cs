using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AskLucy.Application.Authorization;
using AskLucy.Domain.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace AskLucy.Web.Tests;

/// <summary>
/// Mints JWTs signed with the same test key `CustomWebApplicationFactory` configures the
/// host with, so authorization-policy tests (role checks) can run without a live
/// database — role/authentication checks are self-contained in the token's claims and
/// signature, unlike data-fetching endpoints.
///
/// specs/055-role-management: <see cref="AskLucy.Web.Auth.CurrentAuthorizationClaimsTransformation"/>
/// leaves a principal's claims untouched when its subject doesn't resolve to a real Identity row
/// (exactly this factory's synthetic ids) — so a token must arrive with whatever <c>permission</c>
/// claims the test needs already baked in, precisely as an equivalent real Administrator/Super
/// User principal would carry after that transformation runs (full catalogue, research.md
/// Decision 2). <see cref="Create"/> does this automatically for the two built-in role names.
/// </summary>
public static class TestJwtFactory
{
    private const string Issuer = "https://tests.asklucy.io";
    private const string Audience = "https://tests.asklucy.io";
    private const string SigningKey = "test-signing-key-not-for-production-use-minimum-32-chars";

    public static string Create(string userId, params string[] roles) => Create(userId, roles, permissions: []);

    /// <summary>Same as <see cref="Create(string, string[])"/>, plus explicit <c>permission</c> claims — for a synthetic custom-role principal holding only a subset of the catalogue (e.g. <see cref="Admin.PermissionEnforcementMatrixTests"/>).</summary>
    public static string Create(string userId, string[] roles, IEnumerable<string> permissions)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId), .. roles.Select(r => new Claim(ClaimTypes.Role, r))];

        if (roles.Contains("Administrator") || roles.Contains("Super User"))
        {
            claims.AddRange(AdminPermissionCatalog.All.Select(p => new Claim(PermissionClaims.Type, p.Key)));
        }
        else
        {
            claims.AddRange(permissions.Select(key => new Claim(PermissionClaims.Type, key)));
        }

        // Mirrors TokenService's outbound short-claim-name mapping (research.md Topic 1)
        // so test tokens are wire-identical in shape to real ones, not just functionally
        // equivalent for server-side validation.
        var outboundClaims = claims.Select(claim => JwtSecurityTokenHandler.DefaultOutboundClaimTypeMap
            .TryGetValue(claim.Type, out var shortType)
                ? new Claim(shortType, claim.Value, claim.ValueType, claim.Issuer)
                : claim);

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: outboundClaims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
