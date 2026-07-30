using AskLucy.Infrastructure.Consent;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Consent;

public sealed class CookiePolicyProviderTests
{
    [Fact]
    public void GetCurrentPolicy_ShouldReturnTheValuesBoundFromOptions()
    {
        var effectiveAtUtc = new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);
        var provider = new CookiePolicyProvider(Options.Create(new CookiePolicyOptions
        {
            CurrentVersion = "2026-07-30.1",
            EffectiveAtUtc = effectiveAtUtc,
        }));

        var (version, effectiveAt) = provider.GetCurrentPolicy();

        version.Should().Be("2026-07-30.1");
        effectiveAt.Should().Be(effectiveAtUtc);
    }
}
