using AskLucy.Application.Abstractions;
using AskLucy.Application.Consent.Commands.SaveMyCookieConsent;
using AskLucy.Domain.Consent;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Consent;

public sealed class SaveMyCookieConsentCommandHandlerTests
{
    private readonly IUserCookieConsentRepository _consentRepository = Substitute.For<IUserCookieConsentRepository>();
    private readonly ICookiePolicyProvider _policyProvider = Substitute.For<ICookiePolicyProvider>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public SaveMyCookieConsentCommandHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _policyProvider.GetCurrentPolicy().Returns(("2026-07-30.1", DateTime.UtcNow));
    }

    private SaveMyCookieConsentCommandHandler CreateHandler() =>
        new(_consentRepository, _policyProvider, _currentUser, Substitute.For<ILogger<SaveMyCookieConsentCommandHandler>>());

    [Fact]
    public async Task Handle_ShouldInsertANewRecord_StampedWithTheCurrentPolicyVersion()
    {
        CookieConsentRecord? added = null;
        await _consentRepository.AddAsync(Arg.Do<CookieConsentRecord>(r => added = r), Arg.Any<CancellationToken>());

        var result = await CreateHandler().Handle(new SaveMyCookieConsentCommand(true, false, true), CancellationToken.None);

        added.Should().NotBeNull();
        added!.UserId.Should().Be("user-1");
        added.PolicyVersion.Should().Be("2026-07-30.1");
        added.FunctionalAccepted.Should().BeTrue();
        added.AnalyticsAccepted.Should().BeFalse();
        added.MarketingAccepted.Should().BeTrue();

        result.HasConsented.Should().BeTrue();
        result.RequiresReconsent.Should().BeFalse();
        result.Essential.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldNeverCallAnUpdateMethod_OnlyAddAsync()
    {
        await CreateHandler().Handle(new SaveMyCookieConsentCommand(false, false, false), CancellationToken.None);

        await _consentRepository.Received(1).AddAsync(Arg.Any<CookieConsentRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenUserIsUnauthenticated()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => CreateHandler().Handle(new SaveMyCookieConsentCommand(true, true, true), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(false, null, false)]
    [InlineData(false, false, null)]
    public void Validator_ShouldRejectAMissingBooleanField(bool? functional, bool? analytics, bool? marketing)
    {
        var validator = new SaveMyCookieConsentCommandValidator();

        var result = validator.Validate(new SaveMyCookieConsentCommand(functional, analytics, marketing));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ShouldPass_WhenAllFieldsAreProvided()
    {
        var validator = new SaveMyCookieConsentCommandValidator();

        var result = validator.Validate(new SaveMyCookieConsentCommand(true, false, true));

        result.IsValid.Should().BeTrue();
    }
}
