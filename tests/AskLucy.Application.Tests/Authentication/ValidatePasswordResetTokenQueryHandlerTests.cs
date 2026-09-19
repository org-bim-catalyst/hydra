using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Queries.ValidatePasswordResetToken;
using AskLucy.Domain.Authentication;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>
/// Backs the reset page's on-load link check. The rejection cases matter more than the success
/// one: before this existed, a spent link rendered the full "choose a new password" form and only
/// admitted it was dead after the user had picked and confirmed a password.
/// <para>
/// It must also not become a softer oracle than the redeem endpoint — same lookup, same
/// <see cref="PasswordResetToken.CanRedeemFor"/> rule, no new information either way.
/// </para>
/// </summary>
public sealed class ValidatePasswordResetTokenQueryHandlerTests
{
    private const string UserId = "user-1";
    private const string Email = "user@example.com";
    private const string PlaintextToken = "AABBCC";

    private static readonly string TokenHash = new('a', 64);

    private readonly IPasswordResetTokenRepository _tokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly ValidatePasswordResetTokenQueryHandler _handler;

    public ValidatePasswordResetTokenQueryHandlerTests()
    {
        _tokenService.Hash(Arg.Any<string>()).Returns(TokenHash);
        _identityService.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEligibility(Email, EmailConfirmed: true, IsLockedOut: false, HasPassword: true));

        _handler = new ValidatePasswordResetTokenQueryHandler(_tokens, _identityService, _tokenService);
    }

    private static PasswordResetToken PendingToken(string userId = UserId, string email = Email, TimeSpan? lifetime = null) =>
        PasswordResetToken.IssueNew(userId, TokenHash, email, lifetime ?? TimeSpan.FromHours(1), "203.0.113.5");

    private void GivenStoredToken(PasswordResetToken? token) =>
        _tokens.FindByHashAsync(TokenHash, Arg.Any<CancellationToken>()).Returns(token);

    private Task<bool> Handle() =>
        _handler.Handle(new ValidatePasswordResetTokenQuery(UserId, PlaintextToken), CancellationToken.None);

    [Fact]
    public async Task Handle_ShouldAcceptAnUnusedLink()
    {
        GivenStoredToken(PendingToken());

        (await Handle()).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldNotConsumeTheLinkItChecks()
    {
        var token = PendingToken();
        GivenStoredToken(token);

        await Handle();

        token.ConsumedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldRejectALinkThatHasAlreadyBeenUsed()
    {
        var token = PendingToken();
        token.Consume();
        GivenStoredToken(token);

        (await Handle()).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldRejectAnExpiredLink()
    {
        GivenStoredToken(PendingToken(lifetime: TimeSpan.FromSeconds(-1)));

        (await Handle()).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldRejectATokenThatIsNotInTheStore()
    {
        GivenStoredToken(null);

        (await Handle()).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldRejectATokenIssuedForADifferentUser()
    {
        GivenStoredToken(PendingToken(userId: "someone-else"));

        (await Handle()).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldRejectWhenTheAddressHasChangedSinceIssue()
    {
        GivenStoredToken(PendingToken(email: "old-address@example.com"));

        (await Handle()).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldRejectWhenTheAccountNoLongerExists()
    {
        GivenStoredToken(PendingToken());
        _identityService.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((PasswordResetEligibility?)null);

        (await Handle()).Should().BeFalse();
    }
}
