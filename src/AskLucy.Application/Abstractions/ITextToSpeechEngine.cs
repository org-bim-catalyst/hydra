using AskLucy.Application.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// One concrete text-to-speech engine (ElevenLabs, Supertonic, ...) — specs/070. Every engine
/// yields <c>audio/mpeg</c> so the client's single MediaSource pipeline plays any of them, and a
/// mid-reply failover from one engine to another stays seamless.
///
/// Callers never pick an engine directly for Lucy's replies: <see cref="ITextToSpeechProvider"/>
/// (the <c>VoiceProviderRouter</c>) orders engines by the administrator-configured
/// <c>VoiceProvider</c> rows and fails over between them. Only the admin voice page targets a
/// specific engine, to list its voices and preview one.
/// </summary>
public interface ITextToSpeechEngine
{
    /// <summary>Stable key matching <c>VoiceProvider.ProviderKey</c>.</summary>
    string ProviderKey { get; }

    string DisplayName { get; }

    /// <summary>True for a hosted vendor that needs an API key; false for an engine running in-process.</summary>
    bool RequiresCredential { get; }

    /// <param name="apiKey">The decrypted per-provider credential, or null to use the engine's own configured key (if any).</param>
    Task<IReadOnlyList<VoiceOptionDto>> ListVoicesAsync(string? apiKey, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ListVoicesAsync" path="/param[@name='apiKey']"/>
    IAsyncEnumerable<byte[]> StreamSpeechAsync(string text, VoiceSettingsDto settings, string? apiKey, CancellationToken cancellationToken = default);

    /// <summary>This engine's synthesis defaults for <paramref name="language"/>. A non-null
    /// <paramref name="voiceId"/> (the administrator's chosen voice) wins over any configured
    /// per-language voice.</summary>
    VoiceSettingsDto ResolveDefaultSettings(string language, string? voiceId);
}

/// <summary>One selectable voice as listed by an engine — specs/070 contracts/admin-voice.md.</summary>
public sealed record VoiceOptionDto(string Id, string Name, string? Gender, string? Description);
