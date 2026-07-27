using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AskLucy.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Auth;

/// <summary>
/// Pure JWT/refresh-token logic (research.md Topic 1). Full TOTP-continuity coverage
/// (tasks.md T028 — a user with 2FA already enrolled completes login through the new
/// JWT flow without re-enrolling) requires a live database with a seeded user's
/// authenticator secret and is exercised by the Playwright regression matrix
/// (tests/AskLucy.E2E.Tests) instead; it cannot be verified without one.
/// </summary>
public sealed class TokenServiceTests
{
    private readonly TokenService _tokenService = new(Options.Create(new JwtOptions
    {
        Issuer = "https://tests.asklucy.io",
        Audience = "https://tests.asklucy.io",
        SigningKey = "test-signing-key-not-for-production-use-minimum-32-chars",
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 14,
    }));

    [Fact]
    public void GenerateAccessToken_ShouldProduceATokenExpiringInConfiguredWindow()
    {
        var before = DateTime.UtcNow;
        var result = _tokenService.GenerateAccessToken("user-1", [new Claim(ClaimTypes.NameIdentifier, "user-1")]);

        result.AccessToken.Should().NotBeNullOrEmpty();
        result.ExpiresAtUtc.Should().BeCloseTo(before.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void IssueRefreshToken_ShouldGenerateANewFamily_WhenNoneIsProvided()
    {
        var first = _tokenService.IssueRefreshToken();
        var second = _tokenService.IssueRefreshToken();

        first.TokenFamilyId.Should().NotBe(second.TokenFamilyId);
        first.PlainTextToken.Should().NotBe(second.PlainTextToken);
    }

    [Fact]
    public void IssueRefreshToken_ShouldPreserveTheFamily_WhenRotating()
    {
        var existingFamily = Guid.NewGuid();
        var rotated = _tokenService.IssueRefreshToken(existingFamily);

        rotated.TokenFamilyId.Should().Be(existingFamily);
    }

    [Fact]
    public void Hash_ShouldBeDeterministic_ForTheSameInput()
    {
        _tokenService.Hash("same-token").Should().Be(_tokenService.Hash("same-token"));
    }

    [Fact]
    public void Hash_ShouldDiffer_ForDifferentInputs()
    {
        _tokenService.Hash("token-a").Should().NotBe(_tokenService.Hash("token-b"));
    }

    [Fact]
    public void GenerateAccessToken_ShouldEmitShortConventionalClaimNames_NotLongFormUris()
    {
        // Regression test: JwtSecurityToken(claims:) writes Claim.Type verbatim into the
        // wire payload — passing ClaimTypes.Role/.NameIdentifier directly (as
        // IIdentityService.GetClaimsAsync does) would otherwise put the long-form
        // ".../claims/role" URI in the token instead of "role", silently breaking any
        // JS client that decodes the JWT expecting standard short claim names.
        var result = _tokenService.GenerateAccessToken(
            "user-1", [new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(ClaimTypes.Role, "Administrator")]);

        var payload = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken).Payload;

        payload.Should().ContainKey("role").WhoseValue.Should().Be("Administrator");
        payload.Keys.Should().NotContain(k => k.Contains("schemas", StringComparison.Ordinal));
    }
}
