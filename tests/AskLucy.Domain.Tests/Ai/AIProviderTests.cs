using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Ai;

public sealed class AIProviderTests
{
    [Fact]
    public void Create_ShouldStartDisabledWithUnknownHealth()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");

        provider.ProviderKey.Should().Be("openai");
        provider.DisplayName.Should().Be("OpenAI");
        provider.IsEnabled.Should().BeFalse();
        provider.HealthStatus.Should().Be(ProviderHealthStatus.Unknown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenProviderKeyIsBlank(string blankKey)
    {
        var act = () => AIProvider.Create(blankKey, "OpenAI", "admin-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Enable_ShouldThrow_WhenNoCredentialConfigured()
    {
        // FR-003/FR-004: an administrator cannot enable a provider with nothing to
        // authenticate with.
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");

        var act = () => provider.Enable("admin-1");

        act.Should().Throw<DomainRuleViolationException>();
        provider.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Enable_ShouldSucceed_AfterCredentialIsSet()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");
        provider.SetCredential("ciphertext", "admin-1");

        provider.Enable("admin-1");

        provider.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void ClearCredential_ShouldAlsoDisableTheProvider()
    {
        // contracts/admin.md: a provider cannot stay enabled with no credential.
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");
        provider.SetCredential("ciphertext", "admin-1");
        provider.Enable("admin-1");

        provider.ClearCredential("admin-1");

        provider.IsEnabled.Should().BeFalse();
        provider.CredentialCiphertext.Should().BeNull();
    }

    [Fact]
    public void SetCredential_ShouldRecordRotationTimestamp()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");

        provider.SetCredential("ciphertext", "admin-1");

        provider.CredentialCiphertext.Should().Be("ciphertext");
        provider.CredentialLastRotatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void UpdateHealthStatus_ShouldNotTouchModifiedAudit()
    {
        // Automated health-check pings are distinct from administrator-driven config
        // changes (data-model.md) — ModifiedBy/ModifiedAtUtc must stay whatever an admin
        // last set them to.
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");
        provider.SetCredential("ciphertext", "admin-1");
        var modifiedAtAfterCredentialSet = provider.ModifiedAtUtc;

        provider.UpdateHealthStatus(isHealthy: true, DateTime.UtcNow);

        provider.HealthStatus.Should().Be(ProviderHealthStatus.Healthy);
        provider.ModifiedAtUtc.Should().Be(modifiedAtAfterCredentialSet);
    }

    [Fact]
    public void UpdateHealthStatus_ShouldRecordTheClassification_WhenUnhealthy()
    {
        // specs/043 FR-016: an unexplained red chip is what made a quota problem, a bad key and
        // a disabled billing account indistinguishable on the providers page.
        var provider = AIProvider.Create("google-gemini", "Google Gemini", "admin-1");

        provider.UpdateHealthStatus(
            isHealthy: false, DateTime.UtcNow, AiProviderFailureKind.QuotaExhausted, "Quota exhausted.");

        provider.HealthStatus.Should().Be(ProviderHealthStatus.Unhealthy);
        provider.HealthFailureKind.Should().Be(AiProviderFailureKind.QuotaExhausted);
        provider.HealthFailureReason.Should().Be("Quota exhausted.");
    }

    [Fact]
    public void UpdateHealthStatus_ShouldClearTheClassification_OnRecovery()
    {
        // The invariant that matters: a reason recorded during an outage must not outlive the
        // recovery and keep telling an administrator to fix something already fixed. The entity
        // enforces this itself rather than trusting every caller to pass nulls.
        var provider = AIProvider.Create("google-gemini", "Google Gemini", "admin-1");
        provider.UpdateHealthStatus(false, DateTime.UtcNow, AiProviderFailureKind.CredentialRejected, "Bad key.");

        provider.UpdateHealthStatus(isHealthy: true, DateTime.UtcNow);

        provider.HealthStatus.Should().Be(ProviderHealthStatus.Healthy);
        provider.HealthFailureKind.Should().BeNull();
        provider.HealthFailureReason.Should().BeNull();
    }

    [Fact]
    public void UpdateHealthStatus_ShouldIgnoreAClassification_PassedAlongsideAHealthyResult()
    {
        var provider = AIProvider.Create("google-gemini", "Google Gemini", "admin-1");

        provider.UpdateHealthStatus(
            isHealthy: true, DateTime.UtcNow, AiProviderFailureKind.RateLimited, "Should not be stored.");

        provider.HealthFailureKind.Should().BeNull();
        provider.HealthFailureReason.Should().BeNull();
    }
}
