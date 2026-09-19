using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.PasswordReset;
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
/// specs/058-password-recovery T013. The through-line of every test here is that the job's
/// observable behaviour — it returns, without throwing — is the same in all five cases, and only
/// what it enqueues and persists tells them apart.
/// </summary>
public sealed class PasswordResetIssuanceJobTests
{
    private const string UserId = "user-1";
    private const string Email = "user@example.com";

    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IPasswordResetTokenRepository _tokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IPasswordTokenProtector _protector = Substitute.For<IPasswordTokenProtector>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly PasswordResetIssuanceJob _job;

    public PasswordResetIssuanceJobTests()
    {
        _tokenService.Hash(Arg.Any<string>()).Returns(_ => new string('a', 64));
        _protector.Protect(Arg.Any<string>()).Returns(call => $"protected:{call.Arg<string>()}");

        _job = new PasswordResetIssuanceJob(
            _identityService, _tokens, _tokenService, _protector, _backgroundJobClient, _unitOfWork,
            NullLogger<PasswordResetIssuanceJob>.Instance);
    }

    private void GivenAccount(bool emailConfirmed = true, bool lockedOut = false, bool hasPassword = true)
    {
        _identityService.FindIdByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(UserId);
        _identityService.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEligibility(Email, emailConfirmed, lockedOut, hasPassword));
    }

    private Task Handle() => _job.IssueAsync(Email, "203.0.113.5", CancellationToken.None);

    private void ShouldHaveEnqueuedNothing() =>
        _backgroundJobClient.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());

    private void ShouldHaveEnqueuedOneResetEmail() =>
        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Method.Name == nameof(IPasswordEmailJob.SendResetLinkAsync)),
            Arg.Any<IState>());

    [Fact]
    public async Task IssueAsync_ShouldIssueATokenAndEnqueueTheEmail_ForAnEligibleAccount()
    {
        GivenAccount();

        await Handle();

        _tokens.Received(1).Add(Arg.Is<PasswordResetToken>(t => t != null && t.UserId == UserId && t.EmailAtIssue == Email));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        ShouldHaveEnqueuedOneResetEmail();
    }

    [Fact]
    public async Task IssueAsync_ShouldPersistOnlyTheHash_NeverThePlaintextToken()
    {
        GivenAccount();
        string? hashedInput = null;
        _tokenService.Hash(Arg.Do<string>(t => hashedInput = t)).Returns(new string('b', 64));

        await Handle();

        hashedInput.Should().NotBeNullOrWhiteSpace();
        _tokens.Received(1).Add(Arg.Is<PasswordResetToken>(t => t != null && t.TokenHash == new string('b', 64)));
        _tokens.DidNotReceive().Add(Arg.Is<PasswordResetToken>(t => t != null && t.TokenHash == hashedInput));
    }

    [Fact]
    public async Task IssueAsync_ShouldEnqueueTheProtectedToken_NotThePlaintextOne()
    {
        // Hangfire serialises job arguments into its SQL job store, so plaintext there would put a
        // usable reset link in the database that deliberately holds only hashes (FR-016).
        GivenAccount();
        string? plaintext = null;
        _protector.Protect(Arg.Do<string>(t => plaintext = t)).Returns("protected-token");

        await Handle();

        plaintext.Should().NotBeNullOrWhiteSpace();
        var capturedPlaintext = plaintext!;
        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Args.Contains("protected-token") && !j.Args.Contains(capturedPlaintext)),
            Arg.Any<IState>());
    }

    [Fact]
    public async Task IssueAsync_ShouldDoNothingButSucceed_ForAnAddressWithNoAccount()
    {
        _identityService.FindIdByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns((string?)null);

        await Handle();

        _tokens.DidNotReceive().Add(Arg.Any<PasswordResetToken>());
        ShouldHaveEnqueuedNothing();
    }

    [Fact]
    public async Task IssueAsync_ShouldDoNothingButSucceed_WhenTheEmailWasNeverConfirmed()
    {
        // Otherwise a reset becomes an email-verification bypass.
        GivenAccount(emailConfirmed: false);

        await Handle();

        _tokens.DidNotReceive().Add(Arg.Any<PasswordResetToken>());
        ShouldHaveEnqueuedNothing();
    }

    [Fact]
    public async Task IssueAsync_ShouldDoNothingButSucceed_WhenTheAccountIsLockedOut()
    {
        GivenAccount(lockedOut: true);

        await Handle();

        _tokens.DidNotReceive().Add(Arg.Any<PasswordResetToken>());
        ShouldHaveEnqueuedNothing();
    }

    [Fact]
    public async Task IssueAsync_ShouldSupersedePendingTokens_SoAnOlderLinkStopsWorking()
    {
        GivenAccount();

        await Handle();

        await _tokens.Received(1).SupersedePendingForUserAsync(UserId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueAsync_ShouldThrottleSilently_OnTheFourthRequestInsideTheWindow()
    {
        GivenAccount();
        _tokens.CountIssuedSinceAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(3);

        await Handle();

        // Silently: a visible 429 keyed to an email address would itself reveal that the account
        // exists (research.md Topic 3).
        _tokens.DidNotReceive().Add(Arg.Any<PasswordResetToken>());
        ShouldHaveEnqueuedNothing();
    }

    [Fact]
    public async Task IssueAsync_ShouldStillIssue_OnTheThirdRequestInsideTheWindow()
    {
        GivenAccount();
        _tokens.CountIssuedSinceAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(2);

        await Handle();

        _tokens.Received(1).Add(Arg.Any<PasswordResetToken>());
        ShouldHaveEnqueuedOneResetEmail();
    }

    [Fact]
    public async Task IssueAsync_ShouldIssueATokenThatExpires_SoAnInboxCopyGoesStale()
    {
        GivenAccount();
        PasswordResetToken? issued = null;
        _tokens.Add(Arg.Do<PasswordResetToken>(t => issued = t));

        await Handle();

        issued.Should().NotBeNull();
        issued!.ExpiresAtUtc.Should().BeCloseTo(
            issued.CreatedAtUtc.Add(PasswordResetIssuanceJob.TokenLifetime), TimeSpan.FromSeconds(1));
    }
}
