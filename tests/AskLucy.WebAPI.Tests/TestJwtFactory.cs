using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AskLucy.WebAPI.Tests;

/// <summary>
/// Mints JWTs signed with the same test key `CustomWebApplicationFactory` configures the
/// host with, so authorization-policy tests (role checks) can run without a live
/// database — role/authentication checks are self-contained in the token's claims and
/// signature, unlike data-fetching endpoints.
/// </summary>
public static class TestJwtFactory
{
    private const string Issuer = "https://tests.asklucy.io";
    private const string Audience = "https://tests.asklucy.io";
    private const string SigningKey = "test-signing-key-not-for-production-use-minimum-32-chars";

    public static string Create(string userId, params string[] roles)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId), .. roles.Select(r => new Claim(ClaimTypes.Role, r))];

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
