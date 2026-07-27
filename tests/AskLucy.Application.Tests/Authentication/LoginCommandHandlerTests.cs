using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.Commands.Login;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

public sealed class LoginCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        var tokenIssuer = new TokenIssuer(_tokenService, _refreshTokenRepository, _unitOfWork);
        _handler = new LoginCommandHandler(_identityService, tokenIssuer);
    }

    [Fact]
    public async Task Handle_ShouldIssueTokens_WhenCredentialsAreValid()
    {
        _identityService.ValidateCredentialsAsync("user@example.com", "Password1!", Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-1", [new Claim(ClaimTypes.NameIdentifier, "user-1")]));

        _tokenService.GenerateAccessToken("user-1", Arg.Any<IEnumerable<Claim>>())
            .Returns(new AccessTokenResult("access-token", DateTime.UtcNow.AddMinutes(15)));
        _tokenService.IssueRefreshToken(null)
            .Returns(new IssuedRefreshToken("refresh-token", "hash", Guid.NewGuid(), TimeSpan.FromDays(14)));

        var result = await _handler.Handle(new LoginCommand("user@example.com", "Password1!"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.Success);
        result.AccessToken.Should().Be("access-token");
        result.RefreshToken.Should().Be("refresh-token");
    }

    [Fact]
    public async Task Handle_ShouldReturnRequiresTwoFactor_WhenIdentityServiceRequestsIt()
    {
        _identityService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.RequiresTwoFactor, "user-1"));

        var result = await _handler.Handle(new LoginCommand("user@example.com", "Password1!"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.RequiresTwoFactor);
        result.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnInvalidCredentials_WhenPasswordIsWrong()
    {
        _identityService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.InvalidCredentials));

        var result = await _handler.Handle(new LoginCommand("user@example.com", "wrong"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.InvalidCredentials);
    }
}
