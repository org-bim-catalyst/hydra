using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/070 — the router orders the engines by the administrator's voice provider rows
/// and fails over only while no audio has been heard.</summary>
public sealed class VoiceProviderRouterTests
{
    private readonly IVoiceProviderRepository _repository = Substitute.For<IVoiceProviderRepository>();
    private readonly IAiCredentialProtector _protector = Substitute.For<IAiCredentialProtector>();

    public VoiceProviderRouterTests()
    {
        _protector.Unprotect(Arg.Any<string>()).Returns(call => call.Arg<string>().Replace("protected:", string.Empty, StringComparison.Ordinal));
    }

    private VoiceProviderRouter CreateRouter(params FakeEngine[] engines) =>
        new(_repository, engines, _protector, NullLogger<VoiceProviderRouter>.Instance);

    private void Configure(params VoiceProvider[] rows) =>
        _repository.ListByPriorityAsync(Arg.Any<CancellationToken>()).Returns(rows);

    private static VoiceProvider Row(string key, int priority, string? voiceId = null, string? ciphertext = null)
    {
        var row = VoiceProvider.Create(key, key, priority, "system");
        if (voiceId is not null)
        {
            row.SetDefaultVoice(voiceId, "system");
        }

        if (ciphertext is not null)
        {
            row.SetCredential(ciphertext, null, "system");
        }

        return row;
    }

    private static async Task<byte[]> DrainAsync(IAsyncEnumerable<byte[]> stream)
    {
        var bytes = new List<byte>();
        await foreach (var chunk in stream)
        {
            bytes.AddRange(chunk);
        }

        return [.. bytes];
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldSpeakWithThePrimary_AndLeaveTheFailoverAlone()
    {
        var primary = FakeEngine.Speaking("Supertonic", [1, 2], [3]);
        var failover = FakeEngine.Speaking("ElevenLabs", [9]);
        Configure(Row("Supertonic", 0, "F1"), Row("ElevenLabs", 1));
        var router = CreateRouter(failover, primary);

        var settings = await router.ResolveDefaultSettingsAsync("en", CancellationToken.None);
        var audio = await DrainAsync(router.StreamSpeechAsync("Hello.", settings, CancellationToken.None));

        audio.Should().Equal(1, 2, 3);
        settings.ProviderKey.Should().Be("Supertonic");
        settings.VoiceId.Should().Be("F1");
        failover.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldFailOver_WhenThePrimaryFailsBeforeAnyAudio()
    {
        var primary = FakeEngine.Failing("Supertonic", new AiProviderUnavailableException("model missing"));
        var failover = FakeEngine.Speaking("ElevenLabs", [7]);
        Configure(Row("Supertonic", 0, "F1"), Row("ElevenLabs", 1, "rachel", "protected:key-1"));
        var router = CreateRouter(primary, failover);

        var settings = await router.ResolveDefaultSettingsAsync("ar", CancellationToken.None);
        var audio = await DrainAsync(router.StreamSpeechAsync("مرحبا", settings, CancellationToken.None));

        audio.Should().Equal(7);
        var call = failover.Calls.Should().ContainSingle().Subject;
        call.Settings.ProviderKey.Should().Be("ElevenLabs");
        call.Settings.VoiceId.Should().Be("rachel", "the failover speaks with its own voice, not the primary's");
        call.Settings.Language.Should().Be("ar");
        call.ApiKey.Should().Be("key-1");
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldSkipAFailedEngine_ForTheRestOfTheRequest()
    {
        var primary = FakeEngine.Failing("Supertonic", new AiProviderUnavailableException("model missing"));
        var failover = FakeEngine.Speaking("ElevenLabs", [7]);
        Configure(Row("Supertonic", 0), Row("ElevenLabs", 1));
        var router = CreateRouter(primary, failover);
        var settings = await router.ResolveDefaultSettingsAsync("en", CancellationToken.None);

        await DrainAsync(router.StreamSpeechAsync("First sentence.", settings, CancellationToken.None));
        await DrainAsync(router.StreamSpeechAsync("Second sentence.", settings, CancellationToken.None));

        primary.Calls.Should().ContainSingle();
        failover.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldRethrow_WhenTheEngineFailsAfterAudioStarted()
    {
        var primary = FakeEngine.FailingAfter("Supertonic", [1], new AiProviderUnavailableException("inference failed"));
        var failover = FakeEngine.Speaking("ElevenLabs", [7]);
        Configure(Row("Supertonic", 0), Row("ElevenLabs", 1));
        var router = CreateRouter(primary, failover);
        var settings = await router.ResolveDefaultSettingsAsync("en", CancellationToken.None);

        var act = () => DrainAsync(router.StreamSpeechAsync("Hello.", settings, CancellationToken.None));

        await act.Should().ThrowAsync<AiProviderUnavailableException>().WithMessage("inference failed");
        failover.Calls.Should().BeEmpty("the listener already heard the primary's voice");
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldThrowUnavailable_WhenEveryEngineFails()
    {
        var primary = FakeEngine.Failing("Supertonic", new AiProviderUnavailableException("model missing"));
        var failover = FakeEngine.Failing("ElevenLabs", new AiProviderRateLimitedException("quota"));
        Configure(Row("Supertonic", 0), Row("ElevenLabs", 1));
        var router = CreateRouter(primary, failover);
        var settings = await router.ResolveDefaultSettingsAsync("en", CancellationToken.None);

        var act = () => DrainAsync(router.StreamSpeechAsync("Hello.", settings, CancellationToken.None));

        (await act.Should().ThrowAsync<AiProviderUnavailableException>().WithMessage("Every configured voice provider failed."))
            .Which.InnerException.Should().BeOfType<AiProviderRateLimitedException>();
    }

    [Fact]
    public async Task NoConfiguredProvider_ShouldResolveEmptySettings_ButFailTheStream()
    {
        Configure();
        var router = CreateRouter(FakeEngine.Speaking("Supertonic", [1]));

        var settings = await router.ResolveDefaultSettingsAsync("en", CancellationToken.None);
        var act = () => DrainAsync(router.StreamSpeechAsync("Hello.", settings, CancellationToken.None));

        settings.ProviderKey.Should().BeNull();
        await act.Should().ThrowAsync<AiProviderUnavailableException>().WithMessage("No voice provider is configured.");
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldKeepTheCallersOverrides_ForTheEngineTheyWereResolvedFor()
    {
        var primary = FakeEngine.Speaking("Supertonic", [1]);
        Configure(Row("Supertonic", 0, "F1"));
        var router = CreateRouter(primary);
        var settings = await router.ResolveDefaultSettingsAsync("en", CancellationToken.None) with { VoiceId = "user-override", Speed = 1.2 };

        await DrainAsync(router.StreamSpeechAsync("Hello.", settings, CancellationToken.None));

        var call = primary.Calls.Should().ContainSingle().Subject;
        call.Settings.VoiceId.Should().Be("user-override");
        call.Settings.Speed.Should().Be(1.2);
        call.Settings.FallbackVoiceId.Should().Be("F1", "an override meant for another engine needs somewhere to land");
    }

    [Fact]
    public async Task StreamSpeechAsync_ShouldFailOver_WhenTheStoredCredentialCannotBeDecrypted()
    {
        _protector.Unprotect("corrupt").Returns(_ => throw new CryptographicException("key ring changed"));
        var primary = FakeEngine.Speaking("ElevenLabs", [1]);
        var failover = FakeEngine.Speaking("Supertonic", [2]);
        Configure(Row("ElevenLabs", 0, ciphertext: "corrupt"), Row("Supertonic", 1));
        var router = CreateRouter(primary, failover);
        var settings = await router.ResolveDefaultSettingsAsync("en", CancellationToken.None);

        var audio = await DrainAsync(router.StreamSpeechAsync("Hello.", settings, CancellationToken.None));

        audio.Should().Equal(2);
        primary.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ARowWithNoRegisteredEngine_ShouldBeSkipped()
    {
        var engine = FakeEngine.Speaking("Supertonic", [5]);
        Configure(Row("Retired", 0), Row("Supertonic", 1));
        var router = CreateRouter(engine);

        var settings = await router.ResolveDefaultSettingsAsync("en", CancellationToken.None);
        var audio = await DrainAsync(router.StreamSpeechAsync("Hello.", settings, CancellationToken.None));

        settings.ProviderKey.Should().Be("Supertonic");
        audio.Should().Equal(5);
    }

    private sealed class FakeEngine(string providerKey, Func<IAsyncEnumerable<byte[]>> speak) : ITextToSpeechEngine
    {
        public List<(VoiceSettingsDto Settings, string? ApiKey)> Calls { get; } = [];

        public string ProviderKey => providerKey;

        public string DisplayName => providerKey;

        public bool RequiresCredential => false;

        public static FakeEngine Speaking(string key, params byte[][] chunks) => new(key, () => Yield(chunks, null));

        public static FakeEngine Failing(string key, Exception failure) => new(key, () => Yield([], failure));

        public static FakeEngine FailingAfter(string key, byte[] first, Exception failure) => new(key, () => Yield([first], failure));

        public Task<IReadOnlyList<VoiceOptionDto>> ListVoicesAsync(string? apiKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VoiceOptionDto>>([]);

        public VoiceSettingsDto ResolveDefaultSettings(string language, string? voiceId) =>
            new(voiceId ?? $"{providerKey}-default", "model", 0, 0, 0, 1.0, false, "mp3", language, providerKey);

        public IAsyncEnumerable<byte[]> StreamSpeechAsync(
            string text, VoiceSettingsDto settings, string? apiKey, CancellationToken cancellationToken = default)
        {
            Calls.Add((settings, apiKey));
            return speak();
        }

        private static async IAsyncEnumerable<byte[]> Yield(byte[][] chunks, Exception? failure)
        {
            await Task.Yield();
            foreach (var chunk in chunks)
            {
                yield return chunk;
            }

            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
