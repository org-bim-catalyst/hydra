using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.ChangePassword;
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
/// specs/058-password-recovery T038/T047. The distinguishing rule against a reset is FR-010: the
/// session doing the changing survives, every other one does not.
/// </summary>
public sealed class ChangePasswordCommandHandlerTests
{
    private const string UserId = "user-1";
    private const string Email = "user@example.com";
    private const string CurrentPassword = "Current-Passw0rd!";
    private const string NewPassword = "Brand-New-Passw0rd!";
    private const string ActingRefreshToken = "acting-refresh-token";

    private static readonly Guid ActingFamilyId = Guid.CreateVersion7();
    private static readonly string ActingTokenHash = new('f', 64);

    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordResetTokenRepository _resetTokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ISessionRevocationCache _sessionRevocationCache = Substitute.For<ISessionRevocationCache>();
    private readonly ChangePasswordCommandHandler _handler;

    public ChangePasswordCommandHandlerTests()
    {
        GivenAccount(hasPassword: true);
        _tokenService.Hash(ActingRefreshToken).Returns(ActingTokenHash);
        _refreshTokens.ListActiveByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);
        _identityService.VerifyPasswordAsync(UserId, CurrentPassword, Arg.Any<CancellationToken>()).Returns(true);
        _identityService.ResetPasswordAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success));
        _identityService.SetPasswordAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success));

        _handler = new ChangePasswordCommandHandler(
            _identityService, _refreshTokens, _resetTokens, _tokenService, _sessionRevocationCache, _backgroundJobClient, _unitOfWork,
            NullLogger<ChangePasswordCommandHandler>.Instance);
    }

    private void GivenAccount(bool hasPassword) =>
        _identityService.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEligibility(Email, EmailConfirmed: true, IsLockedOut: false, hasPassword));

    private Task<ChangePasswordResult> Handle(string? currentPassword = CurrentPassword) =>
        _handler.Handle(
            new ChangePasswordCommand(UserId, currentPassword, NewPassword, ActingRefreshToken),
            CancellationToken.None);

    private static RefreshToken SessionIn(Guid familyId) =>
        RefreshToken.IssueNew(UserId, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), familyId, TimeSpan.FromDays(14));

    [Fact]
    public async Task Handle_ShouldChangeThePassword_WhenTheCurrentOneIsCorrect()
    {
        (await Handle()).Outcome.Should().Be(ChangePasswordOutcome.Success);

        await _identityService.Received(1).ResetPasswordAsync(UserId, NewPassword, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldSpareTheActingSession_AndRevokeEveryOther()
    {
        var acting = SessionIn(ActingFamilyId);
        var otherA = SessionIn(Guid.CreateVersion7());
        var otherB = SessionIn(Guid.CreateVersion7());

        _refreshTokens.FindByHashAsync(ActingTokenHash, Arg.Any<CancellationToken>()).Returns(acting);
        _refreshTokens.ListActiveByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns([acting, otherA, otherB]);

        await Handle();

        acting.RevokedAtUtc.Should().BeNull("the session making the change must stay signed in (FR-010)");
        otherA.RevokedAtUtc.Should().NotBeNull();
        otherB.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ShouldRevokeEverySession_WhenTheActingTokenCannotBeResolved()
    {
        var session = SessionIn(Guid.CreateVersion7());
        _refreshTokens.FindByHashAsync(ActingTokenHash, Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);
        _refreshTokens.ListActiveByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns([session]);

        await Handle();

        // Failing safe: an unidentifiable caller gets no exemption.
        session.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ShouldSupersedeOutstandingResetLinks()
    {
        await Handle();

        await _resetTokens.Received(1).SupersedePendingForUserAsync(UserId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldEnqueueTheNotificationEmail()
    {
        await Handle();

        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Method.Name == nameof(IPasswordEmailJob.SendPasswordChangedNoticeAsync)),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldRejectAWrongCurrentPassword()
    {
        _identityService.VerifyPasswordAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        (await Handle("not-my-password")).Outcome.Should().Be(ChangePasswordOutcome.CurrentPasswordIncorrect);
        await _identityService.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRequireTheCurrentPassword_WhenTheAccountHasOne()
    {
        (await Handle(currentPassword: null)).Outcome.Should().Be(ChangePasswordOutcome.CurrentPasswordRequired);
        await _identityService.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldRejectANewPasswordEqualToTheCurrentOne()
    {
        var result = await _handler.Handle(
            new ChangePasswordCommand(UserId, CurrentPassword, CurrentPassword, ActingRefreshToken),
            CancellationToken.None);

        result.Outcome.Should().Be(ChangePasswordOutcome.SameAsCurrentPassword);
    }

    [Fact]
    public async Task Handle_ShouldReturnThePerRulePolicyFailures()
    {
        _identityService.ResetPasswordAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Failed, Errors: ["Passwords must have at least one digit."]));

        var result = await Handle();

        result.Outcome.Should().Be(ChangePasswordOutcome.PasswordPolicyViolation);
        result.Errors.Should().ContainSingle();
        _backgroundJobClient.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldSetAFirstPassword_WithoutAskingForACurrentOne()
    {
        GivenAccount(hasPassword: false);

        (await Handle(currentPassword: null)).Outcome.Should().Be(ChangePasswordOutcome.Success);

        await _identityService.Received(1).SetPasswordAsync(UserId, NewPassword, Arg.Any<CancellationToken>());
        await _identityService.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
