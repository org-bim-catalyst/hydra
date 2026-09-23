using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.CustomModels;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.CustomModels;

/// <summary>specs/072 contracts/admin-custom-models.md — <c>backsEngine</c> names the on-server
/// engine a model was deployed for, so the admin can see what making it Available affects.</summary>
public sealed class CustomModelSummaryBuilderTests
{
    private readonly IHostedModelEngine _supertonic = Substitute.For<IHostedModelEngine>();

    public CustomModelSummaryBuilderTests()
    {
        _supertonic.EngineName.Returns("Supertonic");
        _supertonic.ModelRepositoryId.Returns("Supertone/supertonic-3");
    }

    private CustomModelSummaryBuilder CreateBuilder() => new(Substitute.For<IUserAdminRepository>(), [_supertonic]);

    private static CustomModel Model(string sourceUrl, string destination)
    {
        HuggingFaceModelSource.TryParse(sourceUrl, out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate(destination, CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var target, out _).Should().BeTrue();
        return CustomModel.Create(destination.Replace('/', '-'), source!, target!, "admin-1");
    }

    [Fact]
    public async Task BuildAsync_ShouldNameTheHostedEngine_ForAModelFromItsRepository_IgnoringCase()
    {
        var summaries = await CreateBuilder().BuildAsync(
            [
                Model("https://huggingface.co/Supertone/supertonic-3", "Models/supertonic-3"),
                Model("https://huggingface.co/supertone/SUPERTONIC-3", "Models/supertonic-3-upper"),
            ],
            TestContext.Current.CancellationToken);

        summaries.Select(s => s.BacksEngine).Should().Equal("Supertonic", "Supertonic");
    }

    [Fact]
    public async Task BuildAsync_ShouldLeaveBacksEngineNull_ForAnyOtherRepository()
    {
        var summary = await CreateBuilder().BuildAsync(
            Model("https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2", "Models/minilm"),
            TestContext.Current.CancellationToken);

        summary.BacksEngine.Should().BeNull();
    }
}
