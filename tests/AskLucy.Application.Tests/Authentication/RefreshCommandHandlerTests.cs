using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.Commands.Refresh;
using AskLucy.Domain.Authentication;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>
/// Refresh token rotation and reuse-detection (constitution &#167;8, research.md Topic 1) —
/// the single highest-value security test in the auth surface.
/// </summary>
public sealed class RefreshCommandHandlerTests
{
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RefreshCommandHandler _handler;

    public RefreshCommandHandlerTests()
    {
        var tokenIssuer = new TokenIssuer(_tokenService, _refreshTokenRepository, _unitOfWork);
        _handler = new RefreshCommandHandler(_tokenService, _refreshTokenRepository, _identityService, _unitOfWork, tokenIssuer);
        _tokenService.Hash(Arg.Any<string>()).Returns(callInfo => $"hash-of-{callInfo.Arg<string>()}");
    }

    [Fact]
    public async Task Handle_ShouldRotateToken_WhenTokenIsActive()
    {
        var familyId = Guid.NewGuid();
        var active = RefreshToken.IssueNew("user-1", "hash-of-valid-token", familyId, TimeSpan.FromDays(14));
        _refreshTokenRepository.FindByHashAsync("hash-of-valid-token", Arg.Any<CancellationToken>()).Returns(active);
        _identityService.GetClaimsAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") });
        _tokenService.GenerateAccessToken("user-1", Arg.Any<IEnumerable<Claim>>())
            .Returns(new AccessTokenResult("new-access-token", DateTime.UtcNow.AddMinutes(15)));
        _tokenService.IssueRefreshToken(familyId)
            .Returns(new IssuedRefreshToken("new-refresh-token", "hash-of-new-refresh-token", familyId, TimeSpan.FromDays(14)));

        var result = await _handler.Handle(new RefreshCommand("valid-token"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.Success);
        result.RefreshToken.Should().Be("new-refresh-token");
        active.IsActive.Should().BeFalse("the old token must be revoked once rotated");
    }

    [Fact]
    public async Task Handle_ShouldRevokeEntireFamily_WhenARevokedTokenIsReused()
    {
        var familyId = Guid.NewGuid();
        var alreadyRevoked = RefreshToken.IssueNew("user-1", "hash-of-stolen-token", familyId, TimeSpan.FromDays(14));
        alreadyRevoked.Revoke();

        var siblingStillActive = RefreshToken.IssueNew("user-1", "hash-of-sibling-token", familyId, TimeSpan.FromDays(14));

        _refreshTokenRepository.FindByHashAsync("hash-of-stolen-token", Arg.Any<CancellationToken>()).Returns(alreadyRevoked);
        _refreshTokenRepository.ListByFamilyAsync(familyId, Arg.Any<CancellationToken>())
            .Returns([alreadyRevoked, siblingStillActive]);

        var result = await _handler.Handle(new RefreshCommand("stolen-token"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.InvalidCredentials);
        siblingStillActive.IsActive.Should().BeFalse("reuse of a revoked token must revoke the whole family, not just the reused token");
    }

    [Fact]
    public async Task Handle_ShouldReturnInvalidCredentials_WhenTokenIsUnknown()
    {
        _refreshTokenRepository.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);

        var result = await _handler.Handle(new RefreshCommand("unknown-token"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.InvalidCredentials);
    }
}
