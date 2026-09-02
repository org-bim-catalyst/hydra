using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.CheckAiProviderHealth;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/043 US3 (FR-024/FR-026) — an administrator can confirm a fix immediately instead of
/// waiting for the background cycle, and every probe appends one immutable history row.
/// </summary>
public sealed class CheckAiProviderHealthCommandHandlerTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IProviderHealthCheckRepository _healthChecks = Substitute.For<IProviderHealthCheckRepository>();
    private readonly IAIProviderResolver _resolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly IProviderHealthFreshnessPolicy _freshness = Substitute.For<IProviderHealthFreshnessPolicy>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly AIProvider _provider;
    private readonly CheckAiProviderHealthCommandHandler _handler;

    public CheckAiProviderHealthCommandHandlerTests()
    {
        _provider = AIProvider.Create("google-gemini", "Google Gemini", "admin-1");
        _providers.GetByIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns(_provider);
        _resolver.Resolve("google-gemini").Returns(_aiProvider);
        _currentUser.UserId.Returns("admin-1");
        _freshness.StaleAfterUtc(Arg.Any<DateTime?>()).Returns(call => call.Arg<DateTime?>()?.AddMinutes(6));

        _handler = new CheckAiProviderHealthCommandHandler(
            _providers, _healthChecks, _resolver, _freshness, _unitOfWork, _currentUser,
            Substitute.For<ILogger<CheckAiProviderHealthCommandHandler>>());
    }

    [Fact]
    public async Task Handle_ShouldRecordTheClassification_WhenTheProviderIsFailing()
    {
        _aiProvider.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthResult(false, AiProviderFailureKind.QuotaExhausted, "Quota exhausted."));

        var result = await _handler.Handle(new CheckAiProviderHealthCommand(_provider.Id), CancellationToken.None);

        result.HealthStatus.Should().Be(ProviderHealthStatus.Unhealthy);
        result.HealthFailureKind.Should().Be(AiProviderFailureKind.QuotaExhausted);
        result.HealthFailureReason.Should().Be("Quota exhausted.");
        result.HealthStaleAfterUtc.Should().Be(result.CheckedAtUtc.AddMinutes(6));
    }

    [Fact]
    public async Task Handle_ShouldClearAPreviousClassification_WhenTheProviderRecovers()
    {
        // The point of US3: after replacing a credential, the administrator must see the fix
        // reflected - not the reason that was true a moment ago.
        _provider.UpdateHealthStatus(false, DateTime.UtcNow.AddMinutes(-10), AiProviderFailureKind.CredentialRejected, "Bad key.");
        _aiProvider.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthResult(true, null, null));

        var result = await _handler.Handle(new CheckAiProviderHealthCommand(_provider.Id), CancellationToken.None);

        result.HealthStatus.Should().Be(ProviderHealthStatus.Healthy);
        result.HealthFailureKind.Should().BeNull();
        result.HealthFailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ShouldAppendExactlyOneHistoryRow_PerProbe()
    {
        // FR-026 - the append-only audit record.
        _aiProvider.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthResult(true, null, null));

        await _handler.Handle(new CheckAiProviderHealthCommand(_provider.Id), CancellationToken.None);

        _healthChecks.Received(1).Add(Arg.Any<ProviderHealthCheck>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldSucceed_EvenWhenTheProbeFindsTheProviderUnhealthy()
    {
        // A failing provider is a finding, not an error: the check itself worked, so the caller
        // gets a result rather than an exception (contracts/admin-provider-health-api.md §2).
        _aiProvider.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new ProviderHealthResult(false, AiProviderFailureKind.CredentialRejected, "Bad key."));

        var act = () => _handler.Handle(new CheckAiProviderHealthCommand(_provider.Id), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_ShouldNotRecordAnything_WhenTheCheckMechanismItselfFails()
    {
        // FR-023: a failure that is not the provider's must never be written down as the
        // provider being unhealthy - that is how a healthy provider acquires a permanent red
        // chip nobody can explain.
        _aiProvider.CheckHealthAsync(Arg.Any<CancellationToken>())
            .Returns<ProviderHealthResult>(_ => throw new InvalidOperationException("resolver blew up"));

        var act = () => _handler.Handle(new CheckAiProviderHealthCommand(_provider.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _healthChecks.DidNotReceive().Add(Arg.Any<ProviderHealthCheck>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _provider.HealthStatus.Should().Be(ProviderHealthStatus.Unknown);
    }

    [Fact]
    public async Task Handle_ShouldThrowNotFound_ForAnUnknownProvider()
    {
        var act = () => _handler.Handle(new CheckAiProviderHealthCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldRequireAnAuthenticatedActor()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => _handler.Handle(new CheckAiProviderHealthCommand(_provider.Id), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
