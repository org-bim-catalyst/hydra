using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Queries.GetAdminAiProviders;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/043 US2 (FR-017/FR-019/FR-020) — the providers list must carry the reason behind a
/// status and the instant that status stops being trustworthy, so a two-day-old result can
/// never be presented as current fact.
/// </summary>
public sealed class GetAdminAiProvidersQueryHandlerTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IProviderHealthFreshnessPolicy _freshness = Substitute.For<IProviderHealthFreshnessPolicy>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly GetAdminAiProvidersQueryHandler _handler;

    public GetAdminAiProvidersQueryHandlerTests()
    {
        _handler = new GetAdminAiProvidersQueryHandler(
            _providers, _freshness, new DefaultProviderResolver(_providers, _models));
    }

    [Fact]
    public async Task Handle_ShouldExposeTheFailureClassification_AndItsReason()
    {
        // Without this the page can only render an unexplained red chip - the reason existed in
        // the health-check log all along but never reached the DTO.
        var provider = AIProvider.Create("google-gemini", "Google Gemini", "admin-1");
        var checkedAt = new DateTime(2026, 8, 29, 9, 7, 49, DateTimeKind.Utc);
        provider.UpdateHealthStatus(false, checkedAt, AiProviderFailureKind.QuotaExhausted, "Quota exhausted.");
        _providers.ListAllAsync(Arg.Any<CancellationToken>()).Returns([provider]);
        _freshness.StaleAfterUtc(checkedAt).Returns(checkedAt.AddMinutes(6));

        var result = await _handler.Handle(new GetAdminAiProvidersQuery(), CancellationToken.None);

        var dto = result.Should().ContainSingle().Which;
        dto.HealthFailureKind.Should().Be(AiProviderFailureKind.QuotaExhausted);
        dto.HealthFailureReason.Should().Be("Quota exhausted.");
        dto.HealthStaleAfterUtc.Should().Be(checkedAt.AddMinutes(6));
    }

    [Fact]
    public async Task Handle_ShouldDeriveTheStalenessHorizon_FromThePolicy_NotAHardcodedDuration()
    {
        // FR-019 insists the window track the configured interval: a fixed absolute would mark
        // every provider permanently stale the moment someone widened the interval past it.
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");
        var checkedAt = DateTime.UtcNow;
        provider.UpdateHealthStatus(true, checkedAt);
        _providers.ListAllAsync(Arg.Any<CancellationToken>()).Returns([provider]);
        _freshness.StaleAfterUtc(checkedAt).Returns(checkedAt.AddHours(2));

        var result = await _handler.Handle(new GetAdminAiProvidersQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.HealthStaleAfterUtc.Should().Be(checkedAt.AddHours(2));
        _freshness.Received(1).StaleAfterUtc(checkedAt);
    }

    [Fact]
    public async Task Handle_ShouldReportNoStalenessHorizon_ForAProviderNeverChecked()
    {
        // FR-020: "not yet checked" is a distinct state from "checked and stale", and must not
        // be presented as a failure.
        var provider = AIProvider.Create("anthropic", "Anthropic", "admin-1");
        _providers.ListAllAsync(Arg.Any<CancellationToken>()).Returns([provider]);
        _freshness.StaleAfterUtc(null).Returns((DateTime?)null);

        var result = await _handler.Handle(new GetAdminAiProvidersQuery(), CancellationToken.None);

        var dto = result.Should().ContainSingle().Which;
        dto.HealthStatus.Should().Be(ProviderHealthStatus.Unknown);
        dto.HealthStaleAfterUtc.Should().BeNull();
        dto.HealthFailureKind.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldNeverExposeTheCredentialItself()
    {
        // Pre-existing rule (FR-004/FR-031 of specs/005), restated here because this DTO grew.
        var provider = AIProvider.Create("openai", "OpenAI", "admin-1");
        provider.SetCredential("ciphertext", "admin-1");
        _providers.ListAllAsync(Arg.Any<CancellationToken>()).Returns([provider]);

        var result = await _handler.Handle(new GetAdminAiProvidersQuery(), CancellationToken.None);

        var dto = result.Should().ContainSingle().Which;
        dto.HasCredential.Should().BeTrue();
        dto.ToString().Should().NotContain("ciphertext");
    }
}
