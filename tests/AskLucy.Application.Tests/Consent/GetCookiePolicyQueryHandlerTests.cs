using AskLucy.Application.Abstractions;
using AskLucy.Application.Consent.Queries.GetCookiePolicy;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Consent;

public sealed class GetCookiePolicyQueryHandlerTests
{
    [Fact]
    public async Task Handle_ShouldReturnTheCurrentVersionAndEffectiveDate_WithNoUserContext()
    {
        var policyProvider = Substitute.For<ICookiePolicyProvider>();
        var effectiveAtUtc = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
        policyProvider.GetCurrentPolicy().Returns(("2026-07-30.1", effectiveAtUtc));

        var handler = new GetCookiePolicyQueryHandler(policyProvider);
        var result = await handler.Handle(new GetCookiePolicyQuery(), CancellationToken.None);

        result.Version.Should().Be("2026-07-30.1");
        result.EffectiveAtUtc.Should().Be(effectiveAtUtc);
    }
}
