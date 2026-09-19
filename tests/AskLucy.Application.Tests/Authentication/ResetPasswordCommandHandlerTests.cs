using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.ResetPassword;
using AskLucy.Domain.Authentication;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>
/// specs/058-password-recovery T024. Every rejection path must produce the same
/// <see cref="PasswordResetOutcome.InvalidToken"/> — that sameness is the security property, so it
/// is asserted case by case rather than once.
/// </summary>
public sealed class ResetPasswordCommandHandlerTests
{
    private const string UserId = "user-1";
    private const string Email = "user@example.com";
    private const string PlaintextToken = "AABBCC";
    private const string NewPassword = "N3w-Passw0rd!";

    private static readonly string TokenHash = new('a', 64);

    private readonly IPasswordResetTokenRepository _tokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ResetPasswordCommandHandler _handler;

    public ResetPasswordCommandHandlerTests()
    {
        _tokenService.Hash(Arg.Any<string>()).Returns(TokenHash);
        _refreshTokens.ListActiveByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);
        _identityService.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEligibility(Email, EmailConfirmed: true, IsLockedOut: false, HasPassword: true));
        _identityService.ResetPasswordAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success));
        _identityService.SetPasswordAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success));

        _handler = new ResetPasswordCommandHandler(
            _tokens, _refreshTokens, _identityService, _tokenService, _backgroundJobClient, _unitOfWork,
            NullLogger<ResetPasswordCommandHandler>.Instance);
    }

    private static PasswordResetToken PendingToken(string userId = UserId, string email = Email, TimeSpan? lifetime = null) =>
        PasswordResetToken.IssueNew(userId, TokenHash, email, lifetime ?? TimeSpan.FromHours(1), "203.0.113.5");

    private void GivenStoredToken(PasswordResetToken? token) =>
        _tokens.FindByHashAsync(TokenHash, Arg.Any<CancellationToken>()).Returns(token);

    private Task<PasswordResetResult> Handle() =>
        _handler.Handle(new ResetPasswordCommand(UserId, PlaintextToken, NewPassword), CancellationToken.None);

    private void ShouldNotHaveChangedAnything()
    {
        _identityService.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _backgroundJobClient.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldResetThePasswordAndConsumeTheToken_ForAValidLink()
    {
        var token = PendingToken();
        GivenStoredToken(token);

        var result = await Handle();

        result.Outcome.Should().Be(PasswordResetOutcome.Success);
        token.ConsumedAtUtc.Should().NotBeNull();
        await _identityService.Received(1).ResetPasswordAsync(UserId, NewPassword, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldSupersedeOtherOutstandingLinks_ButNotTheRedeemedOne()
    {
        var token = PendingToken();
        GivenStoredToken(token);

        await Handle();

        await _tokens.Received(1).SupersedePendingForUserAsync(UserId, token.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRevokeEverySession_SoAStolenAccountIsSignedOutEverywhere()
    {
        var sessions = new[]
        {
            RefreshToken.IssueNew(UserId, new string('b', 64), Guid.CreateVersion7(), TimeSpan.FromDays(14)),
            RefreshToken.IssueNew(UserId, new string('c', 64), Guid.CreateVersion7(), TimeSpan.FromDays(14)),
        };

        _refreshTokens.ListActiveByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(sessions);
        GivenStoredToken(PendingToken());

        await Handle();

        sessions.Should().OnlyContain(s => s.RevokedAtUtc != null);
    }

    [Fact]
    public async Task Handle_ShouldEnqueueTheNotificationEmail()
    {
        GivenStoredToken(PendingToken());

        await Handle();

        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Method.Name == nameof(IPasswordEmailJob.SendPasswordChangedNoticeAsync)),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldRejectAnUnknownToken()
    {
        GivenStoredToken(null);

        (await Handle()).Outcome.Should().Be(PasswordResetOutcome.InvalidToken);
        ShouldNotHaveChangedAnything();
    }

    [Fact]
    public async Task Handle_ShouldRejectAConsumedToken()
    {
        var token = PendingToken();
        token.Consume();
        GivenStoredToken(token);

        (await Handle()).Outcome.Should().Be(PasswordResetOutcome.InvalidToken);
        ShouldNotHaveChangedAnything();
    }

    [Fact]
    public async Task Handle_ShouldRejectASupersededToken()
    {
        var token = PendingToken();
        token.Supersede();
        GivenStoredToken(token);

        (await Handle()).Outcome.Should().Be(PasswordResetOutcome.InvalidToken);
        ShouldNotHaveChangedAnything();
    }

    [Fact]
    public async Task Handle_ShouldRejectAnExpiredToken()
    {
        GivenStoredToken(PendingToken(lifetime: TimeSpan.FromHours(-1)));

        (await Handle()).Outcome.Should().Be(PasswordResetOutcome.InvalidToken);
        ShouldNotHaveChangedAnything();
    }

    [Fact]
    public async Task Handle_ShouldRejectATokenIssuedToAnAddressTheAccountNoLongerHas()
    {
        GivenStoredToken(PendingToken(email: "old-address@example.com"));

        (await Handle()).Outcome.Should().Be(PasswordResetOutcome.InvalidToken);
        ShouldNotHaveChangedAnything();
    }

    [Fact]
    public async Task Handle_ShouldRejectATokenBelongingToAnotherAccount()
    {
        GivenStoredToken(PendingToken(userId: "someone-else"));

        (await Handle()).Outcome.Should().Be(PasswordResetOutcome.InvalidToken);
        ShouldNotHaveChangedAnything();
    }

    [Fact]
    public async Task Handle_ShouldReturnThePerRulePolicyFailures_AndLeaveTheLinkUsable()
    {
        var token = PendingToken();
        GivenStoredToken(token);
        _identityService.ResetPasswordAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(
                IdentityResultStatus.Failed,
                Errors: ["Passwords must be at least 8 characters.", "Passwords must have at least one digit."]));

        var result = await Handle();

        result.Outcome.Should().Be(PasswordResetOutcome.PasswordPolicyViolation);
        result.Errors.Should().HaveCount(2);
        token.ConsumedAtUtc.Should().BeNull();
        _backgroundJobClient.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldSetAFirstPassword_ForAnExternalOnlyAccount()
    {
        // specs/058-password-recovery T047/FR-014: nothing to replace, so the set-password path.
        _identityService.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEligibility(Email, EmailConfirmed: true, IsLockedOut: false, HasPassword: false));
        GivenStoredToken(PendingToken());

        (await Handle()).Outcome.Should().Be(PasswordResetOutcome.Success);

        await _identityService.Received(1).SetPasswordAsync(UserId, NewPassword, Arg.Any<CancellationToken>());
        await _identityService.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
