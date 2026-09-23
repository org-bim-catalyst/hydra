using System.Runtime.CompilerServices;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.CustomModels.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Ai.Supertonic;

/// <summary>
/// Lucy's in-process voice (specs/070): Supertonic 3, open weights, run on the web server's own
/// CPU — no per-character bill and no third party hearing the reply. Needs no credential.
///
/// Synthesizes one text chunk at a time and yields each as soon as it is encoded, all into a
/// single continuous MP3 stream, so the first sentence plays while later ones are still being
/// generated. The voice styles are fixed presets (F1–F5, M1–M5); the ElevenLabs-shaped tuning
/// fields in <see cref="VoiceSettingsDto"/> have no equivalent here and are ignored, apart from
/// <see cref="VoiceSettingsDto.Speed"/>.
///
/// <para>Its model can be deployed as a custom model (specs/072), so it is also an
/// <see cref="IHostedModelEngine"/>: each request resolves the model's folder once.</para>
/// </summary>
internal sealed partial class SupertonicTextToSpeechEngine(
    SupertonicModel model,
    IOptions<SupertonicOptions> options,
    ILogger<SupertonicTextToSpeechEngine> logger) : ITextToSpeechEngine, IHostedModelEngine
{
    public const string Key = "Supertonic";

    private const string ModelId = "supertonic-3";
    private const float MinimumSpeed = 0.7f;
    private const float MaximumSpeed = 1.5f;
    private const double ChunkSilenceSeconds = 0.3;

    private readonly SupertonicOptions _options = options.Value;

    public string ProviderKey => Key;

    public string DisplayName => "Supertonic (on-server)";

    public bool RequiresCredential => false;

    public string EngineName => Key;

    public string ModelRepositoryId => SupertonicModel.RepositoryId;

    public Task<string?> FindModelProblemAsync(CancellationToken cancellationToken = default) =>
        model.FindModelProblemAsync(cancellationToken);

    public async Task<IReadOnlyList<VoiceOptionDto>> ListVoicesAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        var install = await model.ResolveInstallAsync(cancellationToken);
        return [.. install.Voices.Select(Describe)];
    }

    public VoiceSettingsDto ResolveDefaultSettings(string language, string? voiceId) =>
        new(
            !string.IsNullOrWhiteSpace(voiceId) ? voiceId : _options.DefaultVoice,
            ModelId, 0, 0, 0, _options.Speed, false, $"mp3_{_options.Mp3BitRate}kbps",
            language, Key);

    public async IAsyncEnumerable<byte[]> StreamSpeechAsync(
        string text,
        VoiceSettingsDto settings,
        string? apiKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var language = ResolveLanguage(settings.Language);
        var chunks = SupertonicText.Chunk(text, language);
        if (chunks.Count == 0)
        {
            yield break;
        }

        var install = await model.ResolveInstallAsync(cancellationToken);
        var voice = await model.LoadVoiceAsync(install, ResolveVoiceId(install.Voices, settings), cancellationToken);
        var speed = Math.Clamp((float)settings.Speed, MinimumSpeed, MaximumSpeed);

        using var encoder = new Mp3StreamEncoder(voice.SampleRate, _options.Mp3BitRate);
        var silence = new float[(int)(ChunkSilenceSeconds * voice.SampleRate)];

        for (var i = 0; i < chunks.Count; i++)
        {
            var samples = await model.SynthesizeAsync(chunks[i], language, voice, speed, cancellationToken);
            var encoded = i == 0
                ? encoder.Encode(samples)
                : [.. encoder.Encode(silence), .. encoder.Encode(samples)];

            if (encoded.Length > 0)
            {
                yield return encoded;
            }
        }

        var tail = encoder.Finish();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    private string ResolveLanguage(string? language)
    {
        var resolved = SupertonicText.ResolveLanguage(language);
        if (resolved is not null)
        {
            return resolved;
        }

        Log.LanguageUnsupported(logger, language ?? "(none)", SupertonicText.FallbackLanguage);
        return SupertonicText.FallbackLanguage;
    }

    /// <summary>The requested voice if installed, else the administrator's choice for this engine,
    /// else the configured default, else whatever is installed first. A user's free-text voice
    /// override written for another engine lands here as an unknown id.</summary>
    private string ResolveVoiceId(IReadOnlyList<string> installed, VoiceSettingsDto settings)
    {
        if (installed.Count == 0)
        {
            throw new AiProviderUnavailableException("The Supertonic voice model has no voice styles installed.");
        }

        string? Installed(string? id) =>
            string.IsNullOrWhiteSpace(id) ? null : installed.FirstOrDefault(v => string.Equals(v, id, StringComparison.OrdinalIgnoreCase));

        var requested = Installed(settings.VoiceId);
        if (requested is not null)
        {
            return requested;
        }

        var fallback = Installed(settings.FallbackVoiceId) ?? Installed(_options.DefaultVoice) ?? installed[0];
        if (!string.IsNullOrWhiteSpace(settings.VoiceId))
        {
            Log.VoiceNotInstalled(logger, settings.VoiceId, fallback);
        }

        return fallback;
    }

    /// <summary>Supertonic's presets are named F1–F5 and M1–M5; anything else an operator drops
    /// into <c>voice_styles/</c> is listed under its own file name.</summary>
    private static VoiceOptionDto Describe(string id)
    {
        if (id.Length >= 2 && char.IsDigit(id[1]))
        {
            var gender = char.ToUpperInvariant(id[0]) switch
            {
                'F' => "female",
                'M' => "male",
                _ => null,
            };

            if (gender is not null)
            {
                var name = $"{(gender == "female" ? "Female" : "Male")} {id[1..]}";
                return new VoiceOptionDto(id, name, gender, "Supertonic 3 preset voice");
            }
        }

        return new VoiceOptionDto(id, id, null, "Custom voice style");
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Supertonic does not speak language {Language}; synthesizing as {Fallback}")]
        public static partial void LanguageUnsupported(ILogger logger, string language, string fallback);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Supertonic voice {VoiceId} is not installed; speaking with {Fallback}")]
        public static partial void VoiceNotInstalled(ILogger logger, string voiceId, string fallback);
    }
}
