using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AskLucy.Infrastructure.Auth;

/// <summary>
/// Pure JWT access-token and refresh-token issuance (research.md Topic 1). Takes only
/// primitives/claims — never <c>ApplicationUser</c> — so it has no dependency on Persistence.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessTokenResult GenerateAccessToken(string userId, IEnumerable<Claim> claims)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        // The JwtSecurityToken(claims:) constructor writes each Claim.Type verbatim as
        // the token's property name — it does NOT apply JwtSecurityTokenHandler's
        // outbound short-name mapping the way SecurityTokenDescriptor-based creation
        // does. Left alone, long-form ClaimTypes.* URIs (e.g. ".../claims/role") end up
        // in the wire payload instead of the conventional short JWT claim names
        // ("role", "nameid") that a JS client can read directly. Remap explicitly so the
        // token stays idiomatic on the wire while server-side code can keep using
        // ClaimTypes.* (ASP.NET Core's inbound validation maps these same short names
        // back to ClaimTypes.* by default, so RequireRole/FindFirstValue keep working).
        var outboundClaims = claims.Select(claim => JwtSecurityTokenHandler.DefaultOutboundClaimTypeMap
            .TryGetValue(claim.Type, out var shortType)
                ? new Claim(shortType, claim.Value, claim.ValueType, claim.Issuer)
                : claim);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: outboundClaims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessTokenResult(accessToken, expiresAtUtc);
    }

    public IssuedRefreshToken IssueRefreshToken(Guid? existingTokenFamilyId = null)
    {
        var plainTextToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var lifetime = TimeSpan.FromDays(_options.RefreshTokenLifetimeDays);

        return new IssuedRefreshToken(
            plainTextToken,
            Hash(plainTextToken),
            existingTokenFamilyId ?? Guid.CreateVersion7(),
            lifetime);
    }

    public string Hash(string plainTextToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plainTextToken));
        return Convert.ToHexString(bytes);
    }
}
