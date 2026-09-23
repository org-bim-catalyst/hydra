using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;
using AskLucy.Infrastructure.CustomModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.CustomModels;

/// <summary>specs/072 research D9 (FR-036 to FR-039) — which folder a hosted engine loads from.</summary>
public sealed class ScopedHostedModelLocatorTests
{
    private const string RepositoryId = "Supertone/supertonic-3";

    private readonly ICustomModelRepository _repository = Substitute.For<ICustomModelRepository>();
    private int _scopesCreated;

    private ScopedHostedModelLocator CreateLocator()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            Interlocked.Increment(ref _scopesCreated);
            return _repository;
        });

        return new ScopedHostedModelLocator(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
    }

    private void Completed(params CustomModel[] models) =>
        _repository.FindCompletedForRepositoryAsync(RepositoryId, Arg.Any<CancellationToken>()).Returns(models);

    private static CustomModel CompletedModel(string destination, bool available, string sourceUrl = "https://huggingface.co/Supertone/supertonic-3")
    {
        HuggingFaceModelSource.TryParse(sourceUrl, out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate(destination, AskLucy.Application.Options.CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var target, out _).Should().BeTrue();

        var model = CustomModel.Create(destination.Replace('/', '-'), source!, target!, "admin-1");
        model.StartListing(DateTime.UtcNow);
        model.BeginTransfer(new string('a', 40), 10, 1, 100);
        model.RecordProgress(10, 1, null, null, null);
        model.Complete(DateTime.UtcNow);
        if (available)
        {
            model.MakeAvailable();
        }

        return model;
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnNoRecord_WhenNoCompletedModelExists()
    {
        // The repository only returns Completed, non-deleted records, so this also covers a first
        // deployment that is Queued, running, Failed or Cancelled: the engine keeps its folder (FR-039).
        Completed();

        var resolution = await CreateLocator().ResolveAsync(RepositoryId, TestContext.Current.CancellationToken);

        resolution.Should().BeOfType<HostedModelResolution.NoRecord>();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnTheAvailableModelsFolder()
    {
        var available = CompletedModel("Models/supertonic-3-v2", available: true);
        Completed(CompletedModel("Models/supertonic-3-v1", available: false), available);

        var resolution = await CreateLocator().ResolveAsync(RepositoryId, TestContext.Current.CancellationToken);

        resolution.Should().Be(new HostedModelResolution.Available("Models/supertonic-3-v2", available.Id));
    }

    [Fact]
    public async Task ResolveAsync_ShouldMatchAModelTypedWithDifferentCasing()
    {
        // The database compares ignoring case; the locator must not re-check the id ordinally.
        var available = CompletedModel("Models/supertonic-3", available: true, "https://huggingface.co/supertone/SUPERTONIC-3");
        Completed(available);

        var resolution = await CreateLocator().ResolveAsync(RepositoryId, TestContext.Current.CancellationToken);

        resolution.Should().BeOfType<HostedModelResolution.Available>();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnUnavailable_WhenNoCompletedModelIsAvailable()
    {
        Completed(CompletedModel("Models/supertonic-3", available: false));

        var resolution = await CreateLocator().ResolveAsync(RepositoryId, TestContext.Current.CancellationToken);

        resolution.Should().BeOfType<HostedModelResolution.Unavailable>()
            .Which.Reason.Should().Contain("unavailable in Custom Models");
    }

    [Fact]
    public async Task ResolveAsync_ShouldOpenANewScopeForEachCall_AndSeeAChangeOnTheNextCall()
    {
        var locator = CreateLocator();
        Completed(CompletedModel("Models/supertonic-3", available: true));
        (await locator.ResolveAsync(RepositoryId, TestContext.Current.CancellationToken))
            .Should().BeOfType<HostedModelResolution.Available>();

        Completed(CompletedModel("Models/supertonic-3", available: false));
        (await locator.ResolveAsync(RepositoryId, TestContext.Current.CancellationToken))
            .Should().BeOfType<HostedModelResolution.Unavailable>("nothing is cached (FR-038)");

        _scopesCreated.Should().Be(2);
    }
}
