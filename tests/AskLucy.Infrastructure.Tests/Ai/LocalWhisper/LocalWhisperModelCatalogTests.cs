using AskLucy.Application.Ai.Dictation;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.CustomModels;
using AskLucy.Infrastructure.Ai.LocalWhisper;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai.LocalWhisper;

/// <summary>specs/078 research D5 — which Custom Models deployment Local Whisper can load, and why not.</summary>
public sealed class LocalWhisperModelCatalogTests : IDisposable
{
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";

    private readonly string _root = Directory.CreateTempSubdirectory("asklucy-whisper-catalog-").FullName;
    private readonly ICustomModelRepository _repository = Substitute.For<ICustomModelRepository>();
    private readonly LocalWhisperModelCatalog _catalog;

    public LocalWhisperModelCatalogTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_root);
        _catalog = new LocalWhisperModelCatalog(_repository, environment);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private CustomModel Deployment(string sourceUrl = BaseUrl, bool complete = true, bool available = true)
    {
        HuggingFaceModelSource.TryParse(sourceUrl, out var source, out _).Should().BeTrue();
        DeploymentDestination.TryCreate("Models/whisper-base", CustomModelsOptions.DefaultAllowedDestinationPrefixes, out var target, out _).Should().BeTrue();

        var model = CustomModel.Create("whisper-base", source!, target!, "admin-1");
        if (complete)
        {
            model.StartListing(DateTime.UtcNow);
            model.BeginTransfer(new string('a', 40), 10, 1, 100);
            model.RecordProgress(10, 1, null, null, null);
            model.Complete(DateTime.UtcNow);
            if (available)
            {
                model.MakeAvailable();
            }
        }

        _repository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        return model;
    }

    private string WriteModelFile(byte[] bytes)
    {
        var folder = Path.Combine(_root, "Models", "whisper-base");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "ggml-base.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Ggml() => [.. "lmgg"u8.ToArray(), 1, 2, 3, 4];

    [Fact]
    public async Task ResolveSelectedAsync_ShouldReturnNone_WhenNothingIsSelected()
    {
        var resolution = await _catalog.ResolveSelectedAsync(null, TestContext.Current.CancellationToken);

        resolution.Should().BeSameAs(LocalWhisperModelResolution.None.Instance);
    }

    [Fact]
    public async Task ResolveSelectedAsync_ShouldReturnReady_ForACompletedGgmlFile()
    {
        var model = Deployment();
        var path = WriteModelFile(Ggml());

        var resolution = await _catalog.ResolveSelectedAsync(model.Id, TestContext.Current.CancellationToken);

        resolution.Should().Be(new LocalWhisperModelResolution.Ready(Path.GetFullPath(path), "whisper-base (ggml-base.bin)", "ggml-base.bin"));
    }

    [Fact]
    public async Task ResolveSelectedAsync_ShouldReturnUnavailable_ForAnUnavailableDeployment()
    {
        var model = Deployment(available: false);
        WriteModelFile(Ggml());

        var resolution = await _catalog.ResolveSelectedAsync(model.Id, TestContext.Current.CancellationToken);

        resolution.Should().BeOfType<LocalWhisperModelResolution.Unavailable>();
    }

    [Fact]
    public async Task ResolveSelectedAsync_ShouldReturnUnavailable_WhenTheDeploymentIsGone()
    {
        var resolution = await _catalog.ResolveSelectedAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        resolution.Should().BeOfType<LocalWhisperModelResolution.Unavailable>();
    }

    [Fact]
    public async Task ResolveSelectedAsync_ShouldReturnBroken_WhenTheFileIsMissing()
    {
        var model = Deployment();

        var resolution = await _catalog.ResolveSelectedAsync(model.Id, TestContext.Current.CancellationToken);

        resolution.Should().Be(new LocalWhisperModelResolution.Broken(
            "The model file is missing from its deployment folder.", "whisper-base (ggml-base.bin)"));
    }

    [Fact]
    public async Task ResolveSelectedAsync_ShouldReturnBroken_ForAFileWithoutTheGgmlMagic()
    {
        var model = Deployment();
        WriteModelFile("GGUF\u0003\u0000"u8.ToArray());

        var resolution = await _catalog.ResolveSelectedAsync(model.Id, TestContext.Current.CancellationToken);

        resolution.Should().BeOfType<LocalWhisperModelResolution.Broken>()
            .Which.Reason.Should().Be("The file isn't a Whisper (ggml) model.");
    }

    [Fact]
    public async Task CheckSelectableAsync_ShouldRefuseAWholeRepositoryDeployment()
    {
        var model = Deployment("https://huggingface.co/ggerganov/whisper.cpp");

        var option = await _catalog.CheckSelectableAsync(model.Id, TestContext.Current.CancellationToken);

        option.Selectable.Should().BeFalse();
        option.Reason.Should().Be("Deploy the model from a URL that names its .bin file.");
    }

    [Fact]
    public async Task CheckSelectableAsync_ShouldRefuseADeploymentThatIsNotCompleted()
    {
        var model = Deployment(complete: false);

        var option = await _catalog.CheckSelectableAsync(model.Id, TestContext.Current.CancellationToken);

        option.Selectable.Should().BeFalse();
        option.Reason.Should().Be("Only a completed deployment can be selected.");
    }

    [Fact]
    public async Task CheckSelectableAsync_ShouldAcceptACompletedGgmlFile()
    {
        var model = Deployment();
        WriteModelFile(Ggml());

        var option = await _catalog.CheckSelectableAsync(model.Id, TestContext.Current.CancellationToken);

        option.Should().Be(new LocalWhisperModelOption(model.Id, "whisper-base (ggml-base.bin)", true, null));
    }
}
