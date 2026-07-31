using AskLucy.Application.Abstractions;
using AskLucy.Application.Consent.Queries.GetMyCookieConsent;
using AskLucy.Domain.Consent;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Consent;

public sealed class GetMyCookieConsentQueryHandlerTests
{
    private readonly IUserCookieConsentRepository _consentRepository = Substitute.For<IUserCookieConsentRepository>();
    private readonly ICookiePolicyProvider _policyProvider = Substitute.For<ICookiePolicyProvider>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public GetMyCookieConsentQueryHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _policyProvider.GetCurrentPolicy().Returns(("2026-07-30.1", DateTime.UtcNow));
    }

    private GetMyCookieConsentQueryHandler CreateHandler() => new(_consentRepository, _policyProvider, _currentUser);

    [Fact]
    public async Task Handle_ShouldReturnNotConsentedAndRequiresReconsent_WhenNoRecordExists()
    {
        _consentRepository.GetLatestAsync("user-1", Arg.Any<CancellationToken>()).Returns((CookieConsentRecord?)null);

        var result = await CreateHandler().Handle(new GetMyCookieConsentQuery(), CancellationToken.None);

        result.HasConsented.Should().BeFalse();
        result.RequiresReconsent.Should().BeTrue();
        result.PolicyVersion.Should().BeNull();
        result.CurrentPolicyVersion.Should().Be("2026-07-30.1");
        result.Essential.Should().BeTrue();
        result.Functional.Should().BeFalse();
        result.Analytics.Should().BeFalse();
        result.Marketing.Should().BeFalse();
        result.LastUpdatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldNotRequireReconsent_WhenLatestRecordMatchesCurrentPolicyVersion()
    {
        var record = CookieConsentRecord.Create("user-1", "2026-07-30.1", true, false, true);
        _consentRepository.GetLatestAsync("user-1", Arg.Any<CancellationToken>()).Returns(record);

        var result = await CreateHandler().Handle(new GetMyCookieConsentQuery(), CancellationToken.None);

        result.HasConsented.Should().BeTrue();
        result.RequiresReconsent.Should().BeFalse();
        result.Functional.Should().BeTrue();
        result.Analytics.Should().BeFalse();
        result.Marketing.Should().BeTrue();
        result.LastUpdatedAtUtc.Should().Be(record.CreatedAtUtc);
    }

    [Fact]
    public async Task Handle_ShouldRequireReconsent_WhenLatestRecordVersionDiffersFromCurrent()
    {
        var record = CookieConsentRecord.Create("user-1", "2026-06-01.1", true, true, true);
        _consentRepository.GetLatestAsync("user-1", Arg.Any<CancellationToken>()).Returns(record);

        var result = await CreateHandler().Handle(new GetMyCookieConsentQuery(), CancellationToken.None);

        result.HasConsented.Should().BeTrue();
        result.RequiresReconsent.Should().BeTrue();
        result.PolicyVersion.Should().Be("2026-06-01.1");
        result.CurrentPolicyVersion.Should().Be("2026-07-30.1");
    }

    [Fact]
    public async Task Handle_ShouldThrow_WhenUserIsUnauthenticated()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => CreateHandler().Handle(new GetMyCookieConsentQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
