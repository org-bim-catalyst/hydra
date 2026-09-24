using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai;

/// <summary>
/// specs/070 — the <see cref="ITextToSpeechProvider"/> every voice-output handler speaks through.
/// Orders the registered <see cref="ITextToSpeechEngine"/>s by the administrator-configured
/// <c>VoiceProvider</c> rows (priority 0 is Lucy's voice) and fails over to the next engine when
/// one fails before producing any audio.
///
/// A failure after audio has started is rethrown rather than failed over: the listener has
/// already heard part of the sentence, and replaying it in another voice would be worse than
/// the existing audio-failed path. An engine that fails is skipped for the rest of the request
/// (this class is scoped), so a reply's later sentences don't each wait on the same dead engine.
///
/// Only when every engine has failed does the caller see an exception — always an
/// <see cref="AiProviderUnavailableException"/>, one of the three types
/// <see cref="TextToSpeechStreamer"/> turns into an <c>audio-failed</c> event and a recorded
/// failover to the browser's own voice.
/// </summary>
internal sealed partial class VoiceProviderRouter(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines,
    IAiCredentialProtector credentialProtector,
    IAIProviderRepository aiProviders,
    ILogger<VoiceProviderRouter> logger) : ITextToSpeechProvider
{
    private const string FallbackLanguage = "en";

    private readonly HashSet<string> _failedProviderKeys = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<Candidate>? _candidates;

    public async Task<VoiceSettingsDto> ResolveDefaultSettingsAsync(string language, CancellationToken cancellationToken = default)
    {
        var candidates = await GetCandidatesAsync(cancellationToken);
        var primary = candidates.FirstOrDefault(c => !_failedProviderKeys.Contains(c.Engine.ProviderKey));

        // Never throws for "nothing configured": callers resolve settings before the reply's
        // text streams, and a voice problem must not take the text reply down with it.
        // StreamSpeechAsync below reports the same condition as an audio failure instead.
        return primary is null
            ? new VoiceSettingsDto(string.Empty, string.Empty, 0, 0, 0, 1.0, false, string.Empty, NormalizeLanguage(language))
            : SettingsFor(primary, NormalizeLanguage(language));
    }

    public async IAsyncEnumerable<byte[]> StreamSpeechAsync(
        string textChunk,
        VoiceSettingsDto settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var candidates = await GetCandidatesAsync(cancellationToken);
        var language = NormalizeLanguage(settings.Language);
        Exception? lastFailure = null;

        foreach (var candidate in candidates)
        {
            if (_failedProviderKeys.Contains(candidate.Engine.ProviderKey))
            {
                continue;
            }

            // The caller's settings carry any per-user overrides, but only make sense for the
            // engine they were resolved for; any other engine speaks with its own defaults.
            var attemptSettings = string.Equals(settings.ProviderKey, candidate.Engine.ProviderKey, StringComparison.OrdinalIgnoreCase)
                ? settings with { Language = language, FallbackVoiceId = candidate.DefaultVoiceId }
                : SettingsFor(candidate, language);

            var attempt = await TryStartAsync(candidate, textChunk, attemptSettings, cancellationToken);
            if (attempt.Failure is not null)
            {
                _failedProviderKeys.Add(candidate.Engine.ProviderKey);
                lastFailure = attempt.Failure;
                Log.VoiceProviderFailedOver(logger, candidate.Engine.ProviderKey, attempt.Failure.Message);
                continue;
            }

            if (attempt.Enumerator is null)
            {
                // The engine had nothing to say for this text — not a failure.
                yield break;
            }

            await using (attempt.Enumerator)
            {
                yield return attempt.Enumerator.Current;
                while (await attempt.Enumerator.MoveNextAsync())
                {
                    yield return attempt.Enumerator.Current;
                }
            }

            yield break;
        }

        throw new AiProviderUnavailableException(
            candidates.Count == 0 ? "No voice provider is configured." : "Every configured voice provider failed.",
            lastFailure);
    }

    /// <summary>Starts the engine's stream and pulls its first chunk, so a failure to produce
    /// any audio at all surfaces here — where failing over is still invisible to the listener.</summary>
    private static async Task<StartAttempt> TryStartAsync(
        Candidate candidate, string textChunk, VoiceSettingsDto settings, CancellationToken cancellationToken)
    {
        if (candidate.CredentialFailure is not null)
        {
            return new StartAttempt(null, candidate.CredentialFailure);
        }

        IAsyncEnumerator<byte[]>? enumerator = null;
        try
        {
            enumerator = candidate.Engine
                .StreamSpeechAsync(textChunk, settings, candidate.ApiKey, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            if (await enumerator.MoveNextAsync())
            {
                return new StartAttempt(enumerator, null);
            }

            await enumerator.DisposeAsync();
            return new StartAttempt(null, null);
        }
        catch (AiProviderException ex)
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync();
            }

            return new StartAttempt(null, ex);
        }
    }

    private async Task<IReadOnlyList<Candidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        if (_candidates is not null)
        {
            return _candidates;
        }

        var engineByKey = engines.ToDictionary(e => e.ProviderKey, StringComparer.OrdinalIgnoreCase);
        var rows = await voiceProviders.ListByPriorityAsync(cancellationToken);
        var speechVendors = await VoiceEngineResolution.ListSpeechVendorsAsync(aiProviders, cancellationToken);
        var candidates = new List<Candidate>(rows.Count);

        foreach (var row in rows)
        {
            if (!engineByKey.TryGetValue(row.ProviderKey, out var engine))
            {
                Log.VoiceProviderEngineMissing(logger, row.ProviderKey);
                continue;
            }

            // An administrator switching the vendor off under AI providers takes it out of the
            // order entirely — a deliberate choice, not a failure to fail over from.
            var vendor = VoiceEngineResolution.FindVendor(speechVendors, row.ProviderKey);
            if (vendor is { IsEnabled: false })
            {
                Log.VoiceProviderSwitchedOff(logger, row.ProviderKey);
                continue;
            }

            string? apiKey = null;
            AiProviderException? credentialFailure = null;
            var ciphertext = vendor is null ? row.CredentialCiphertext : vendor.CredentialCiphertext;
            if (ciphertext is not null)
            {
                try
                {
                    apiKey = credentialProtector.Unprotect(ciphertext);
                }
                catch (CryptographicException ex)
                {
                    credentialFailure = new AiProviderCredentialUnreadableException(
                        $"The stored {engine.DisplayName} credential could not be decrypted.", ex);
                }
            }

            candidates.Add(new Candidate(engine, row.DefaultVoiceId, apiKey, credentialFailure));
        }

        _candidates = candidates;
        return candidates;
    }

    private static VoiceSettingsDto SettingsFor(Candidate candidate, string language) =>
        candidate.Engine.ResolveDefaultSettings(language, candidate.DefaultVoiceId) with
        {
            Language = language,
            ProviderKey = candidate.Engine.ProviderKey,
            FallbackVoiceId = candidate.DefaultVoiceId,
        };

    private static string NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? FallbackLanguage : language.Trim();

    private sealed record Candidate(
        ITextToSpeechEngine Engine,
        string? DefaultVoiceId,
        string? ApiKey,
        AiProviderException? CredentialFailure);

    private readonly record struct StartAttempt(IAsyncEnumerator<byte[]>? Enumerator, Exception? Failure);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Voice provider {ProviderKey} could not synthesize speech ({Reason}); failing over to the next voice provider")]
        public static partial void VoiceProviderFailedOver(ILogger logger, string providerKey, string reason);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Voice provider {ProviderKey} is configured but no text-to-speech engine with that key is registered; skipping it")]
        public static partial void VoiceProviderEngineMissing(ILogger logger, string providerKey);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Voice provider {ProviderKey} is switched off under AI providers; skipping it")]
        public static partial void VoiceProviderSwitchedOff(ILogger logger, string providerKey);
    }
}
