using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Locations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Locations;

/// <summary>
/// Regression for the production HTTP 500 on 2026-08-30. SendChatMessageCommandHandler starts
/// location resolution as a concurrent task (specs/037 FR-008) and then immediately streams the
/// model's reply. When both paths run against a provider whose CreateClientAsync reads the
/// encrypted credential from the database — Anthropic and Google Gemini, but not OpenAI, which
/// reads its key from configuration — two EF operations hit the same scoped DbContext at once
/// and it throws "A second operation was started on this context instance". That is an
/// unclassified exception, so the whole turn returned a bare 500 instead of any of the
/// actionable statuses AiProviderResponseClassifier produces.
/// </summary>
public sealed class ScopeIsolatedLocationResolutionServiceTests
{
    [Fact]
    public async Task ResolveAsync_ShouldBuildItsDependencies_InAScopeOfItsOwn()
    {
        // Stands in for the scoped DbContext: anything resolved per-scope proves whether the
        // inner service shared the caller's scope or was given a fresh one.
        var scopedDependencies = new List<IGeocodingProvider>();

        var services = new ServiceCollection();
        services.AddScoped<IGeocodingProvider>(_ =>
        {
            var geocoder = Substitute.For<IGeocodingProvider>();
            scopedDependencies.Add(geocoder);
            return geocoder;
        });
        services.AddScoped(_ => Substitute.For<IAIProviderRepository>());
        services.AddScoped(_ => Substitute.For<IAIModelRepository>());
        services.AddScoped(_ => Substitute.For<IAIProviderResolver>());
        services.AddScoped<DefaultProviderResolver>();
        services.AddScoped(_ => Substitute.For<IAiCapabilityAssignmentRepository>());
        services.AddScoped<AiCapabilityProviderResolver>();
        services.AddSingleton<ILogger<AiCapabilityProviderResolver>>(NullLogger<AiCapabilityProviderResolver>.Instance);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new LocationResolutionOptions()));
        services.AddSingleton<ILogger<LocationResolutionService>>(NullLogger<LocationResolutionService>.Instance);
        services.AddScoped<LocationResolutionService>();
        services.AddScoped<ILocationResolutionService, ScopeIsolatedLocationResolutionService>();

        await using var root = services.BuildServiceProvider();
        using var callerScope = root.CreateScope();

        // The caller's own scope materialises its graph first — as the request pipeline does
        // before the handler ever starts the concurrent task.
        _ = callerScope.ServiceProvider.GetRequiredService<LocationResolutionService>();
        scopedDependencies.Should().HaveCount(1, "the caller's scope built its own dependencies");

        var sut = callerScope.ServiceProvider.GetRequiredService<ILocationResolutionService>();

        // Resolution itself fails (no providers are configured on these substitutes) and is
        // swallowed into Unavailable — irrelevant here. What matters is which scope built the
        // graph, and that happens during construction, before any of that logic runs.
        await sut.ResolveAsync("user-1", Guid.NewGuid(), "Show me Al Safa Park 2", null, TestContext.Current.CancellationToken);

        scopedDependencies.Should().HaveCount(2, "the concurrent work must not reuse the caller's scope");
        scopedDependencies[1].Should().NotBeSameAs(scopedDependencies[0]);
    }
}
