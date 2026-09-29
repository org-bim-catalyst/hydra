using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai.Dictation;
using AskLucy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Dictation;

/// <summary>
/// specs/078 FR-004 — whether a dictation engine's vendor is switched on under Admin → AI
/// providers. Local Whisper and the browser built-in have no vendor, so they always are.
/// </summary>
public sealed class DictationVendorGate(
    IAIProviderRepository aiProviders, ISpeechToTextSessionProvider elevenLabs, IAiCredentialProtector credentialProtector)
{
    public const string OpenAiSwitchedOff = "OpenAI is switched off under AI providers.";
    public const string ElevenLabsSwitchedOff = "ElevenLabs is switched off under AI providers.";

    /// <summary>Null when <paramref name="engine"/> can be chosen; otherwise why not.</summary>
    public async Task<string?> WhyNotSelectableAsync(DictationTurnEngine engine, CancellationToken cancellationToken = default) => engine switch
    {
        DictationTurnEngine.OpenAiWhisper =>
            (await aiProviders.GetByKeyAsync(DictationEngineSetting.OpenAiVendorKey, cancellationToken))?.IsEnabled == true ? null : OpenAiSwitchedOff,

        // The same check the stt-session uses, including its configuration fallback when no row exists.
        DictationTurnEngine.ElevenLabsRealtime =>
            await elevenLabs.IsSwitchedOnAsync(cancellationToken) ? null : ElevenLabsSwitchedOff,

        _ => null,
    };

    /// <summary>
    /// specs/078 research D6 — the stt-session's local health check for OpenAI Whisper: switched on,
    /// and its DB credential (when one is stored) decrypts. No network call is made here; a config
    /// fallback key is assumed fine, and a real failure surfaces on first actual use.
    /// </summary>
    public async Task<string?> OpenAiWhisperHealthProblemAsync(CancellationToken cancellationToken = default)
    {
        var provider = await aiProviders.GetByKeyAsync(DictationEngineSetting.OpenAiVendorKey, cancellationToken);
        if (provider is not { IsEnabled: true })
        {
            return OpenAiSwitchedOff;
        }

        if (provider.CredentialCiphertext is null)
        {
            return null;
        }

        try
        {
            credentialProtector.Unprotect(provider.CredentialCiphertext);
            return null;
        }
        catch (CryptographicException)
        {
            return "The OpenAI credential can't be read.";
        }
    }
}

/// <summary>specs/078 — a stale <c>rowVersion</c> on any dictation-setting write is a 409.</summary>
internal static class DictationSettingConcurrency
{
    public static void EnsureCurrent(DictationEngineSetting setting, string rowVersion)
    {
        if (!setting.RowVersion.AsSpan().SequenceEqual(Convert.FromBase64String(rowVersion)))
        {
            throw new ConcurrencyConflictException("The dictation settings were changed by someone else. Reload and try again.");
        }
    }

    public static bool IsBase64(string? value) =>
        !string.IsNullOrEmpty(value) && Convert.TryFromBase64String(value, new byte[value.Length], out _);
}

/// <summary>specs/078 FR-004/FR-017 — the chosen engine's vendor is switched off (422).</summary>
public sealed class DictationEngineNotSelectableException(string reason) : Exception(reason);

/// <summary>specs/078 FR-009a — the deployment can't be the Local Whisper model, with the reason (422).</summary>
public sealed class LocalWhisperModelNotSelectableException(string reason) : Exception(reason);

/// <summary>specs/078 FR-007 — the selected Local Whisper model's file is missing or unreadable: a Local Whisper failure.</summary>
public sealed class LocalWhisperModelBrokenException(string reason) : Exception(reason);

/// <summary>specs/078 FR-013 — the clip isn't 16 kHz mono 16-bit PCM WAV (422). The message is the detail shown.</summary>
public sealed class DictationAudioInvalidException(string message) : Exception(message);

/// <summary>
/// specs/078 FR-009c — "Try it" couldn't load or run the model (503). Only administrators see
/// this, so the reason is shown; nothing goes on the operational failure trail.
/// </summary>
public sealed class LocalWhisperTrialFailedException(string reason, Exception inner) : Exception(reason, inner);

internal static partial class DictationAdminLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Admin dictation action {Action} performed by {ActorUserId}: {Detail}")]
    public static partial void ActionPerformed(ILogger logger, string action, string actorUserId, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local Whisper try of model {CustomModelId} by {ActorUserId} failed")]
    public static partial void TrialFailed(ILogger logger, Exception exception, Guid customModelId, string actorUserId);
}
