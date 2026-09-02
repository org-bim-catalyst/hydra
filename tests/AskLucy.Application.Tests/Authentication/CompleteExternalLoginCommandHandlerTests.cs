using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.Commands.ExternalLogin;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

public sealed class CompleteExternalLoginCommandHandlerTests
{
    private readonly IExternalLoginCodeStore _codeStore = Substitute.For<IExternalLoginCodeStore>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CompleteExternalLoginCommandHandler _handler;

    public CompleteExternalLoginCommandHandlerTests()
    {
        var tokenIssuer = new TokenIssuer(_tokenService, _refreshTokenRepository, _unitOfWork);
        _handler = new CompleteExternalLoginCommandHandler(_codeStore, _identityService, tokenIssuer);
    }

    [Fact]
    public async Task Handle_ShouldIssueTokens_WhenCodeIsValid()
    {
        _codeStore.TryConsume("valid-code").Returns("user-1");
        _identityService.GetClaimsAsync("user-1", Arg.Any<CancellationToken>())
            .Returns([new Claim(ClaimTypes.NameIdentifier, "user-1")]);
        _tokenService.GenerateAccessToken("user-1", Arg.Any<IEnumerable<Claim>>())
            .Returns(new AccessTokenResult("access-token", DateTime.UtcNow.AddMinutes(15)));
        _tokenService.IssueRefreshToken(null)
            .Returns(new IssuedRefreshToken("refresh-token", "hash", Guid.NewGuid(), TimeSpan.FromDays(14)));

        var result = await _handler.Handle(new CompleteExternalLoginCommand("valid-code"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.Success);
        result.AccessToken.Should().Be("access-token");
        result.RefreshToken.Should().Be("refresh-token");
    }

    [Fact]
    public async Task Handle_ShouldReturnInvalidCredentials_WhenCodeIsUnknownOrAlreadyUsed()
    {
        _codeStore.TryConsume("bad-code").Returns((string?)null);

        var result = await _handler.Handle(new CompleteExternalLoginCommand("bad-code"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.InvalidCredentials);
        await _identityService.DidNotReceiveWithAnyArgs().GetClaimsAsync(default!, TestContext.Current.CancellationToken);
    }
}
