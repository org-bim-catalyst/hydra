using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.Commands.LoginTwoFactor;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

public sealed class LoginTwoFactorCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly LoginTwoFactorCommandHandler _handler;

    public LoginTwoFactorCommandHandlerTests()
    {
        var tokenIssuer = new TokenIssuer(_tokenService, _refreshTokenRepository, _unitOfWork);
        _handler = new LoginTwoFactorCommandHandler(_identityService, tokenIssuer);
    }

    [Fact]
    public async Task Handle_ShouldIssueTokens_WhenCodeIsValid()
    {
        _identityService.ValidateTwoFactorCodeAsync("user-1", "123456", false, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-1", [new Claim(ClaimTypes.NameIdentifier, "user-1")]));
        _tokenService.GenerateAccessToken("user-1", Arg.Any<IEnumerable<Claim>>())
            .Returns(new AccessTokenResult("access-token", DateTime.UtcNow.AddMinutes(15)));
        _tokenService.IssueRefreshToken(null)
            .Returns(new IssuedRefreshToken("refresh-token", "hash", Guid.NewGuid(), TimeSpan.FromDays(14)));

        var result = await _handler.Handle(new LoginTwoFactorCommand("user-1", "123456", false), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.Success);
        result.AccessToken.Should().Be("access-token");
    }

    [Fact]
    public async Task Handle_ShouldReturnInvalidCredentials_WhenCodeIsWrong()
    {
        _identityService.ValidateTwoFactorCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.InvalidCredentials));

        var result = await _handler.Handle(new LoginTwoFactorCommand("user-1", "000000", false), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.InvalidCredentials);
    }

    [Fact]
    public async Task Handle_ShouldAcceptRecoveryCode_WhenIsRecoveryCodeFlagIsSet()
    {
        _identityService.ValidateTwoFactorCodeAsync("user-1", "recovery-code-1", true, Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-1", [new Claim(ClaimTypes.NameIdentifier, "user-1")]));
        _tokenService.GenerateAccessToken(Arg.Any<string>(), Arg.Any<IEnumerable<Claim>>())
            .Returns(new AccessTokenResult("access-token", DateTime.UtcNow.AddMinutes(15)));
        _tokenService.IssueRefreshToken(null)
            .Returns(new IssuedRefreshToken("refresh-token", "hash", Guid.NewGuid(), TimeSpan.FromDays(14)));

        var result = await _handler.Handle(new LoginTwoFactorCommand("user-1", "recovery-code-1", true), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.Success);
        await _identityService.Received(1).ValidateTwoFactorCodeAsync("user-1", "recovery-code-1", true, Arg.Any<CancellationToken>());
    }
}
