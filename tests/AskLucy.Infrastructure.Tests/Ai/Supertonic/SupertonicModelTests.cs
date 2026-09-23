using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Infrastructure.Ai.Supertonic;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai.Supertonic;

/// <summary>specs/072 research D9 — the Supertonic model follows its custom model record. The tests
/// that load real ONNX sessions only run when <c>SUPERTONIC_MODEL_DIRECTORY</c> points at a download
/// from <c>scripts/download-supertonic.ps1</c>.</summary>
public sealed class SupertonicModelTests : IDisposable
{
    private const string ModelDirectoryVariable = "SUPERTONIC_MODEL_DIRECTORY";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "supertonic-model-tests-" + Guid.NewGuid().ToString("N"));
    private readonly IHostedModelLocator _locator = Substitute.For<IHostedModelLocator>();

    public SupertonicModelTests() => Resolve(HostedModelResolution.NoRecord.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Resolve(HostedModelResolution resolution) =>
        _locator.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(resolution);

    private SupertonicModel CreateModel(string configuredDirectory)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_root);
        return new SupertonicModel(
            Options.Create(new SupertonicOptions { ModelDirectory = configuredDirectory }),
            environment,
            _locator,
            NullLogger<SupertonicModel>.Instance);
    }

    private string InstallVoices(string relativeDirectory, params string[] voiceIds)
    {
        var directory = Path.Combine(_root, relativeDirectory);
        Directory.CreateDirectory(Path.Combine(directory, "voice_styles"));
        foreach (var id in voiceIds)
        {
            File.WriteAllText(Path.Combine(directory, "voice_styles", id + ".json"), "{}");
        }

        return directory;
    }

    [Fact]
    public async Task ResolveInstallAsync_ShouldUseTheConfiguredFolder_WhenNoCustomModelRecordExists()
    {
        var configured = InstallVoices("App_Data/Models/supertonic-3", "F1");
        using var model = CreateModel("App_Data/Models/supertonic-3");

        var install = await model.ResolveInstallAsync(TestContext.Current.CancellationToken);

        install.Directory.Should().Be(Path.GetFullPath(configured));
        install.Voices.Should().Equal("F1");
        await _locator.Received(1).ResolveAsync("Supertone/supertonic-3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveInstallAsync_ShouldUseTheDeployedFolder_WhenACustomModelIsAvailable()
    {
        InstallVoices("App_Data/Models/supertonic-3", "F1");
        var deployed = InstallVoices("Models/supertonic-3-v2", "M2");
        Resolve(new HostedModelResolution.Available("Models/supertonic-3-v2", Guid.NewGuid()));
        using var model = CreateModel("App_Data/Models/supertonic-3");

        var install = await model.ResolveInstallAsync(TestContext.Current.CancellationToken);

        install.Directory.Should().Be(Path.GetFullPath(deployed));
        install.Voices.Should().Equal("M2");
    }

    [Fact]
    public async Task ResolveInstallAsync_ShouldThrowUnavailable_WhenTheCustomModelIsMarkedUnavailable()
    {
        InstallVoices("App_Data/Models/supertonic-3", "F1");
        Resolve(new HostedModelResolution.Unavailable("The model deployed from Supertone/supertonic-3 is marked unavailable in Custom Models."));
        using var model = CreateModel("App_Data/Models/supertonic-3");

        var act = () => model.ResolveInstallAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Message.Should().Be("The Supertonic model is marked unavailable in Custom Models.");
    }

    [Fact]
    public async Task ResolveInstallAsync_ShouldThrowNotFound_WhenTheDeployedFolderIsMissing()
    {
        Resolve(new HostedModelResolution.Available("Models/gone", Guid.NewGuid()));
        using var model = CreateModel("App_Data/Models/supertonic-3");

        var act = () => model.ResolveInstallAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Message.Should().Contain("not found on this server").And.NotContain(_root, "a physical path must never reach the client");
    }

    [Fact]
    public async Task LoadVoiceAsync_ShouldThrowNotFound_WhenTheDeployedFolderHasNoOnnxFiles()
    {
        InstallVoices("Models/supertonic-3", "F1");
        Resolve(new HostedModelResolution.Available("Models/supertonic-3", Guid.NewGuid()));
        using var model = CreateModel("App_Data/Models/supertonic-3");
        var install = await model.ResolveInstallAsync(TestContext.Current.CancellationToken);

        var act = () => model.LoadVoiceAsync(install, "F1", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Message.Should().Contain("not found on this server").And.NotContain(_root);
    }

    private string InstallComplete(string relativeDirectory)
    {
        var directory = InstallVoices(relativeDirectory, "F1");
        Directory.CreateDirectory(Path.Combine(directory, "onnx"));
        foreach (var file in new[] { "duration_predictor.onnx", "text_encoder.onnx", "vector_estimator.onnx", "vocoder.onnx", "tts.json", "unicode_indexer.json" })
        {
            File.WriteAllText(Path.Combine(directory, "onnx", file), string.Empty);
        }

        return directory;
    }

    [Fact]
    public async Task FindModelProblemAsync_ShouldReportNothing_WithNoCustomModelRecord()
    {
        // FR-039: the configured install is today's behaviour, even when it is missing here.
        using var model = CreateModel("App_Data/Models/supertonic-3");

        (await model.FindModelProblemAsync(TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task FindModelProblemAsync_ShouldReportTheUnavailableReason()
    {
        Resolve(new HostedModelResolution.Unavailable("unavailable"));
        using var model = CreateModel("App_Data/Models/supertonic-3");

        (await model.FindModelProblemAsync(TestContext.Current.CancellationToken))
            .Should().Be("The Supertonic model is marked unavailable in Custom Models.");
    }

    [Fact]
    public async Task FindModelProblemAsync_ShouldReportNothing_WhenTheDeployedFolderIsComplete()
    {
        InstallComplete("Models/supertonic-3");
        Resolve(new HostedModelResolution.Available("Models/supertonic-3", Guid.NewGuid()));
        using var model = CreateModel("App_Data/Models/supertonic-3");

        (await model.FindModelProblemAsync(TestContext.Current.CancellationToken)).Should().BeNull();
        model.LoadedDirectory.Should().BeNull("checking never loads the sessions");
    }

    [Fact]
    public async Task FindModelProblemAsync_ShouldReportMissingFiles_WhenADeployedFileIsGone()
    {
        var deployed = InstallComplete("Models/supertonic-3");
        File.Delete(Path.Combine(deployed, "onnx", "vocoder.onnx"));
        Resolve(new HostedModelResolution.Available("Models/supertonic-3", Guid.NewGuid()));
        using var model = CreateModel("App_Data/Models/supertonic-3");

        (await model.FindModelProblemAsync(TestContext.Current.CancellationToken))
            .Should().Be("The Supertonic model files were not found on this server.");
    }

    [Fact]
    public async Task ADirectoryChange_ShouldDisposeTheLoadedSessions_AndUnavailableShouldKeepThemDisposed()
    {
        var realModel = Environment.GetEnvironmentVariable(ModelDirectoryVariable);
        if (string.IsNullOrWhiteSpace(realModel))
        {
            Assert.Skip($"Set {ModelDirectoryVariable} to a downloaded Supertonic 3 model to run this test.");
        }

        using var model = CreateModel(realModel);
        var ct = TestContext.Current.CancellationToken;

        var configured = await model.ResolveInstallAsync(ct);
        await model.LoadVoiceAsync(configured, configured.Voices[0], ct);
        model.LoadedDirectory.Should().Be(Path.GetFullPath(realModel));

        // Deployed, made Available, but its files aren't on this server: the old sessions still go.
        InstallVoices("Models/supertonic-3", "F1");
        Resolve(new HostedModelResolution.Available("Models/supertonic-3", Guid.NewGuid()));
        var deployed = await model.ResolveInstallAsync(ct);
        await FluentActions.Invoking(() => model.LoadVoiceAsync(deployed, "F1", ct))
            .Should().ThrowAsync<AiProviderUnavailableException>();
        model.LoadedDirectory.Should().BeNull();

        Resolve(HostedModelResolution.NoRecord.Instance);
        var voice = await model.LoadVoiceAsync(await model.ResolveInstallAsync(ct), configured.Voices[0], ct);
        model.LoadedDirectory.Should().Be(Path.GetFullPath(realModel), "the configured folder loads again");

        Resolve(new HostedModelResolution.Unavailable("unavailable"));
        await FluentActions.Invoking(() => model.ResolveInstallAsync(ct)).Should().ThrowAsync<AiProviderUnavailableException>();
        model.LoadedDirectory.Should().BeNull("an Unavailable model frees its sessions");

        // A voice loaded before the switch must not run on disposed sessions.
        await FluentActions.Invoking(() => model.SynthesizeAsync("Hello.", "en", voice, 1f, ct))
            .Should().ThrowAsync<AiProviderUnavailableException>();
    }
}
