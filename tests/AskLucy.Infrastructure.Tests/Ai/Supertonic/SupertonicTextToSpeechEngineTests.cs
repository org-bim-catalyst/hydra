using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Infrastructure.Ai.Supertonic;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai.Supertonic;

/// <summary>specs/070 — the Supertonic engine's behaviour around the model files. The synthesis
/// test needs the real ~400 MB model, so it only runs when <c>SUPERTONIC_MODEL_DIRECTORY</c>
/// points at a download from <c>scripts/download-supertonic.ps1</c>; set
/// <c>SUPERTONIC_TEST_OUTPUT_DIRECTORY</c> as well to keep the MP3s and listen to them.</summary>
public sealed class SupertonicTextToSpeechEngineTests : IDisposable
{
    private const string ModelDirectoryVariable = "SUPERTONIC_MODEL_DIRECTORY";
    private const string OutputDirectoryVariable = "SUPERTONIC_TEST_OUTPUT_DIRECTORY";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "supertonic-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (SupertonicTextToSpeechEngine Engine, SupertonicModel Model) CreateEngine(string modelDirectory)
    {
        var options = Options.Create(new SupertonicOptions { ModelDirectory = modelDirectory });
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());

        var model = new SupertonicModel(options, environment, NullLogger<SupertonicModel>.Instance);
        return (new SupertonicTextToSpeechEngine(model, options, NullLogger<SupertonicTextToSpeechEngine>.Instance), model);
    }

    private void InstallVoiceFiles(params string[] voiceIds)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "voice_styles"));
        foreach (var id in voiceIds)
        {
            File.WriteAllText(Path.Combine(directory.FullName, id + ".json"), "{}");
        }
    }

    [Fact]
    public async Task ListVoicesAsync_ShouldListInstalledStyles_WithFriendlyNames()
    {
        InstallVoiceFiles("M2", "F1", "Studio");
        var (engine, model) = CreateEngine(_root);
        using var _ = model;

        var voices = await engine.ListVoicesAsync(null, CancellationToken.None);

        voices.Should().Equal(
            new VoiceOptionDto("F1", "Female 1", "female", "Supertonic 3 preset voice"),
            new VoiceOptionDto("M2", "Male 2", "male", "Supertonic 3 preset voice"),
            new VoiceOptionDto("Studio", "Studio", null, "Custom voice style"));
    }

    [Fact]
    public async Task ListVoicesAsync_ShouldThrowUnavailable_WhenTheModelIsNotInstalled()
    {
        var (engine, model) = CreateEngine(_root);
        using var _ = model;

        var act = () => engine.ListVoicesAsync(null, CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderUnavailableException>())
            .Which.Message.Should().NotContain(_root, "a physical path must never reach the client");
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldThrowUnavailable_WhenTheOnnxFilesAreMissing()
    {
        InstallVoiceFiles("F1");
        var (engine, model) = CreateEngine(_root);
        using var _ = model;
        var settings = engine.ResolveDefaultSettings("en", null);

        var act = async () =>
        {
            await foreach (var _ in engine.StreamSpeechAsync("Hello.", settings, null, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
    }

    [Fact]
    public void ResolveDefaultSettings_ShouldUseTheChosenVoice_OrTheConfiguredDefault()
    {
        var (engine, model) = CreateEngine(_root);
        using var _ = model;

        engine.ResolveDefaultSettings("ar", "F3").Should().Match<VoiceSettingsDto>(s =>
            s.VoiceId == "F3" && s.Language == "ar" && s.ProviderKey == SupertonicTextToSpeechEngine.Key);
        engine.ResolveDefaultSettings("en", null).VoiceId.Should().Be("F1");
    }

    [Theory]
    [InlineData("en", "The quick brown fox jumps over the lazy dog. It was a sunny day.")]
    [InlineData("ar", "مرحبا، أنا لوسي. كيف يمكنني مساعدتك اليوم؟")]
    public async Task StreamSpeechAsync_ShouldSynthesizeMp3_WithTheRealModel(string language, string text)
    {
        var modelDirectory = Environment.GetEnvironmentVariable(ModelDirectoryVariable);
        if (string.IsNullOrWhiteSpace(modelDirectory))
        {
            Assert.Skip($"Set {ModelDirectoryVariable} to a downloaded Supertonic 3 model to run this test.");
        }

        var (engine, model) = CreateEngine(modelDirectory);
        using var _ = model;

        // An unknown voice id (a user override meant for another engine) must fall back, not fail.
        var settings = engine.ResolveDefaultSettings(language, "some-elevenlabs-voice") with { FallbackVoiceId = "F2" };

        var audio = new List<byte>();
        await foreach (var chunk in engine.StreamSpeechAsync(text, settings, null, TestContext.Current.CancellationToken))
        {
            audio.AddRange(chunk);
        }

        audio.Count.Should().BeGreaterThan(8_000, "a few seconds of speech at 96 kbps");
        audio[0].Should().Be(0xFF);
        (audio[1] & 0xE0).Should().Be(0xE0);

        var outputDirectory = Environment.GetEnvironmentVariable(OutputDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, $"supertonic-{language}.mp3"), [.. audio], TestContext.Current.CancellationToken);
        }
    }
}
