using System.Security.Cryptography;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Authentication;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Identity;

/// <summary>
/// T123 — <see cref="AccountLinkIssuer"/> (specs/067 research R10, FR-009d): one link per kind, minted when
/// the email is sent, on the configured frontend origin; a reset link lives 60 minutes and works once; and
/// an account that may not be sent a link is refused without saying why to anyone but the security log.
/// </summary>
public sealed class AccountLinkIssuerTests
{
    private const string UserId = "user-1";
    private const string Email = "layla@example.com";

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly InMemoryResetTokens _tokens = new();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeLogger<AccountLinkIssuer> _logger = new();

    public AccountLinkIssuerTests()
    {
        _tokenService.Hash(Arg.Any<string>()).Returns(call => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(call.Arg<string>()!))));
        _identity.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEligibility(Email, EmailConfirmed: true, IsLockedOut: false, HasPassword: true));
        _identity.GenerateEmailConfirmationTokenAsync(UserId, Arg.Any<CancellationToken>()).Returns("CONFIRM+/=token");
        _identity.GenerateChangeEmailTokenAsync(UserId, "new@example.com", Arg.Any<CancellationToken>()).Returns("CHANGE+/=token");
    }

    private AccountLinkIssuer CreateSut(string frontend = "https://app.example.test") =>
        new(_identity, _tokens, _tokenService, _unitOfWork, Options.Create(new AppOptions { FrontendBaseUrl = frontend }), _logger);

    private void WithEligibility(bool confirmed = true, bool lockedOut = false) =>
        _identity.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PasswordResetEligibility(Email, confirmed, lockedOut, HasPassword: true));

    private static string TokenOf(Uri link) =>
        System.Web.HttpUtility.ParseQueryString(link.Query)["token"]!;

    // ---- a link per kind ----

    [Fact]
    public async Task EmailConfirmation_IsTheConfirmEmailPage_WithTheUserIdAndTheIdentityToken_EscapedForAUrl()
    {
        WithEligibility(confirmed: false);

        var link = await CreateSut().IssueAsync(SensitiveLinkKind.EmailConfirmation, UserId, Email, isRetry: false, CancellationToken.None);

        link.AbsoluteUri.Should().Be("https://app.example.test/confirm-email?userId=user-1&token=CONFIRM%2B%2F%3Dtoken");
    }

    [Fact]
    public async Task EmailChange_IsTheConfirmEmailChangePage_ForTheNewAddress()
    {
        var link = await CreateSut().IssueAsync(SensitiveLinkKind.EmailChange, UserId, "new@example.com", isRetry: false, CancellationToken.None);

        link.AbsoluteUri.Should().Be("https://app.example.test/confirm-email-change?userId=user-1&newEmail=new%40example.com&token=CHANGE%2B%2F%3Dtoken");
    }

    [Fact]
    public async Task PasswordReset_IsTheResetPasswordPage_WithTheUserIdAndAFreshToken()
    {
        var link = await CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, isRetry: false, CancellationToken.None);

        link.AbsolutePath.Should().Be("/reset-password");
        System.Web.HttpUtility.ParseQueryString(link.Query)["userId"].Should().Be(UserId);
        TokenOf(link).Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public async Task EveryKind_TargetsTheConfiguredFrontendOrigin_WhateverItsTrailingSlash()
    {
        WithEligibility(confirmed: false);
        var sut = CreateSut("https://hydra.bimcatalyst.com/");

        var confirmation = await sut.IssueAsync(SensitiveLinkKind.EmailConfirmation, UserId, Email, false, CancellationToken.None);
        var change = await sut.IssueAsync(SensitiveLinkKind.EmailChange, UserId, "new@example.com", false, CancellationToken.None);
        WithEligibility(confirmed: true);
        var reset = await sut.IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);

        foreach (var link in new[] { confirmation, change, reset })
        {
            link.GetLeftPart(UriPartial.Authority).Should().Be("https://hydra.bimcatalyst.com");
            link.AbsoluteUri.Should().NotContain("//confirm").And.NotContain("//reset");
        }
    }

    // ---- validity ----

    [Fact]
    public async Task PasswordReset_IsValidForSixtyMinutes()
    {
        await CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);

        var issued = _tokens.Added.Should().ContainSingle().Subject;
        (issued.ExpiresAtUtc - issued.CreatedAtUtc).Should().Be(TimeSpan.FromMinutes(60));
        NotificationTypeCatalog.Get(NotificationTypeKeys.AccountPasswordResetRequested).RequestValidity.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void ConfirmationAndEmailChange_AreValidForTwentyFourHours_AsTheCatalogueSaysAndIdentitysDefaultTokenLifespanDoes()
    {
        NotificationTypeCatalog.Get(NotificationTypeKeys.AccountEmailConfirmationRequested).RequestValidity.Should().Be(TimeSpan.FromHours(24));
        NotificationTypeCatalog.Get(NotificationTypeKeys.AccountEmailChangeRequested).RequestValidity.Should().Be(TimeSpan.FromHours(24));
    }

    // ---- single use ----

    [Fact]
    public async Task PasswordReset_CanBeRedeemedOnlyOnce()
    {
        var link = await CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);
        var stored = await _tokens.FindByHashAsync(_tokenService.Hash(TokenOf(link)), CancellationToken.None);

        stored.Should().NotBeNull();
        stored!.CanRedeemFor(Email).Should().BeTrue();
        stored.Consume();

        stored.CanRedeemFor(Email).Should().BeFalse("a second redemption of the same link must fail");
    }

    [Fact]
    public async Task PasswordReset_ANewLinkSupersedesTheOneBeforeIt()
    {
        var sut = CreateSut();
        var first = await sut.IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);
        var second = await sut.IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);

        TokenOf(second).Should().NotBe(TokenOf(first));
        var firstRow = await _tokens.FindByHashAsync(_tokenService.Hash(TokenOf(first)), CancellationToken.None);
        var secondRow = await _tokens.FindByHashAsync(_tokenService.Hash(TokenOf(second)), CancellationToken.None);
        firstRow!.CanRedeemFor(Email).Should().BeFalse("a forwarded older email must not still work (FR-006)");
        secondRow!.CanRedeemFor(Email).Should().BeTrue();
    }

    [Fact]
    public async Task PasswordReset_StoresOnlyTheHashOfTheToken_AndPersistsItBeforeReturningTheLink()
    {
        var link = await CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);

        var stored = _tokens.Added.Single();
        stored.TokenHash.Should().Be(_tokenService.Hash(TokenOf(link))).And.NotBe(TokenOf(link));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- refusals ----

    [Fact]
    public async Task PasswordReset_ForAnUnconfirmedAccount_IsRefused_BecauseItWouldBeAnEmailVerificationBypass()
    {
        WithEligibility(confirmed: false);

        var act = () => CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);

        await act.Should().ThrowAsync<AccountLinkRefusedException>();
        _tokens.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task PasswordReset_ForALockedOutAccount_IsRefused()
    {
        WithEligibility(lockedOut: true);

        var act = () => CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);

        await act.Should().ThrowAsync<AccountLinkRefusedException>();
    }

    [Fact]
    public async Task PasswordReset_ForAnAccountThatNoLongerExists_IsRefused()
    {
        _identity.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>()).Returns((PasswordResetEligibility?)null);

        var act = () => CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);

        await act.Should().ThrowAsync<AccountLinkRefusedException>();
    }

    [Fact]
    public async Task PasswordReset_FourthNewRequestInFifteenMinutes_IsThrottled_ButARetryOfAnExistingDeliveryIsNot()
    {
        var sut = CreateSut();
        for (var i = 0; i < 3; i++)
        {
            await sut.IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);
        }

        var throttled = () => sut.IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);
        await throttled.Should().ThrowAsync<AccountLinkRefusedException>();

        var retried = await sut.IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, isRetry: true, CancellationToken.None);
        retried.Should().NotBeNull();
    }

    [Fact]
    public async Task EmailConfirmation_ForAnAlreadyConfirmedAccount_IsRefused()
    {
        WithEligibility(confirmed: true);

        var act = () => CreateSut().IssueAsync(SensitiveLinkKind.EmailConfirmation, UserId, Email, false, CancellationToken.None);

        await act.Should().ThrowAsync<AccountLinkRefusedException>();
    }

    [Fact]
    public async Task EveryRefusal_SaysTheSameThing_SoNothingRevealsWhichAccountStateAppliedOrWhetherTheAddressHasAnAccount()
    {
        var messages = new List<string>();
        foreach (var setup in new Action[]
        {
            () => WithEligibility(confirmed: false),
            () => WithEligibility(lockedOut: true),
            () => _identity.GetPasswordResetEligibilityAsync(UserId, Arg.Any<CancellationToken>()).Returns((PasswordResetEligibility?)null),
        })
        {
            setup();
            var ex = await Assert.ThrowsAsync<AccountLinkRefusedException>(() =>
                CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None));
            messages.Add(ex.Message);
        }

        messages.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task EveryRefusal_IsInTheSecurityLog_WithTheRealReason_AndNoAddress()
    {
        WithEligibility(lockedOut: true);

        await Assert.ThrowsAsync<AccountLinkRefusedException>(() =>
            CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None));

        var entry = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        entry.Message.Should().Contain("locked").And.Contain(UserId).And.NotContain(Email);
    }

    // ---- nothing leaks ----

    [Fact]
    public async Task NoLogLine_CarriesTheTokenOrTheLink()
    {
        var link = await CreateSut().IssueAsync(SensitiveLinkKind.PasswordReset, UserId, Email, false, CancellationToken.None);

        var logged = string.Join('\n', _logger.Collector.GetSnapshot().Select(r => r.Message));
        logged.Should().NotContain(TokenOf(link)).And.NotContain("reset-password").And.NotContain(Email);
    }

    [Fact]
    public async Task EmailConfirmationAndChange_LogNeitherTheirTokenNorTheirLink()
    {
        WithEligibility(confirmed: false);
        var sut = CreateSut();
        await sut.IssueAsync(SensitiveLinkKind.EmailConfirmation, UserId, Email, false, CancellationToken.None);
        await sut.IssueAsync(SensitiveLinkKind.EmailChange, UserId, "new@example.com", false, CancellationToken.None);

        var logged = string.Join('\n', _logger.Collector.GetSnapshot().Select(r => r.Message));
        logged.Should().NotContain("CONFIRM").And.NotContain("CHANGE+").And.NotContain("new@example.com");
    }

    /// <summary>The slice of the token repository the issuer uses, in memory.</summary>
    private sealed class InMemoryResetTokens : IPasswordResetTokenRepository
    {
        private readonly List<PasswordResetToken> _all = [];

        public IReadOnlyList<PasswordResetToken> Added => _all;

        public void Add(PasswordResetToken token) => _all.Add(token);

        public Task<PasswordResetToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(_all.SingleOrDefault(t => t.TokenHash == tokenHash));

        public Task<int> CountIssuedSinceAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(_all.Count(t => t.UserId == userId && t.CreatedAtUtc >= sinceUtc));

        public Task SupersedePendingForUserAsync(string userId, Guid? exceptTokenId = null, CancellationToken cancellationToken = default)
        {
            foreach (var token in _all.Where(t => t.UserId == userId && t.Id != exceptTokenId))
            {
                token.Supersede();
            }

            return Task.CompletedTask;
        }

        public Task<int> DeleteSpentBeforeAsync(DateTime createdBeforeUtc, int batchSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
