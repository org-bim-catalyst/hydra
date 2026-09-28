using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai.Dictation;

/// <summary>
/// specs/078 — the platform-wide dictation choice: the primary engine, the Push-to-Talk engine
/// used while ElevenLabs realtime is primary, and the selected Local Whisper model. There is
/// exactly one row, keyed by <see cref="SingletonId"/>.
/// <para>
/// A fresh deployment has no Local Whisper model selected, so dictation uses the browser built-in
/// as its normal path until an administrator deploys and selects one (FR-002). Whatever is chosen,
/// a failure only ever falls to the browser built-in, never to another cloud engine (FR-005).
/// </para>
/// </summary>
public sealed class DictationEngineSetting : BaseEntity
{
    public static readonly Guid SingletonId = new("0f7a1c52-3b6e-4d18-9a07-8e5d0c078001");

    public const int MaxSuspensionReasonLength = 500;

    /// <summary>The <c>AIProvider.ProviderKey</c> of the vendor behind OpenAI Whisper.</summary>
    public const string OpenAiVendorKey = "openai";

    /// <summary>The <c>AIProvider.ProviderKey</c> of the vendor behind ElevenLabs realtime.</summary>
    public const string ElevenLabsVendorKey = "elevenlabs";

    public const string SystemActor = "system";

    private DictationEngineSetting()
    {
        // Required by EF Core materialization.
    }

    public DictationPrimaryEngine PrimaryEngine { get; private set; }

    /// <summary>Used only while <see cref="PrimaryEngine"/> is ElevenLabs realtime (FR-017).</summary>
    public DictationClipEngine PushToTalkEngine { get; private set; }

    /// <summary>A <c>CustomModels.Id</c>, or null when none is selected (the browser built-in serves Local Whisper's paths).</summary>
    public Guid? LocalWhisperModelId { get; private set; }

    public DictationEngineState State { get; private set; }

    public DateTime? SuspendedAtUtc { get; private set; }

    public string? SuspensionReason { get; private set; }

    public DictationPrimaryEngine? SuspendedEngine { get; private set; }

    public DateTime? LastRevertedAtUtc { get; private set; }

    public DictationRevertReason? LastRevertReason { get; private set; }

    public DictationPrimaryEngine? LastRevertedFrom { get; private set; }

    public static DictationEngineSetting CreateDefault(DateTime utcNow) => new()
    {
        Id = SingletonId,
        PrimaryEngine = DictationPrimaryEngine.LocalWhisper,
        PushToTalkEngine = DictationClipEngine.LocalWhisper,
        State = DictationEngineState.Active,
        CreatedAtUtc = utcNow,
        CreatedBy = SystemActor,
    };

    /// <summary>
    /// Sets the primary engine. The caller has already checked its vendor is switched on (FR-004).
    /// Always clears a suspension, even when the same engine is set again after a renewal (FR-016).
    /// </summary>
    public void SetPrimary(DictationPrimaryEngine engine, string actor, DateTime utcNow)
    {
        PrimaryEngine = engine;
        ClearSuspension();
        Touch(actor, utcNow);
    }

    /// <summary>
    /// Sets the Push-to-Talk engine (FR-017). The caller has already checked OpenAI is switched on
    /// for OpenAI Whisper. Clears a suspension of that engine's vendor.
    /// </summary>
    public void SetPushToTalkEngine(DictationClipEngine engine, string actor, DateTime utcNow)
    {
        PushToTalkEngine = engine;

        var vendor = VendorOf(ToTurnEngine(engine));
        if (vendor is not null && SuspendedEngine is { } suspended && vendor == VendorOf(ToTurnEngine(suspended)))
        {
            ClearSuspension();
        }

        Touch(actor, utcNow);
    }

    /// <summary>Selects the Local Whisper model; null means none. The caller has already checked the deployment is selectable.</summary>
    public void SelectLocalWhisperModel(Guid? customModelId, string actor, DateTime utcNow)
    {
        LocalWhisperModelId = customModelId;
        Touch(actor, utcNow);
    }

    public bool IsSelected(Guid customModelId) => LocalWhisperModelId == customModelId;

    /// <summary>
    /// The engine for one dictation turn. Continuous uses the primary engine. Push-to-Talk uses it
    /// too, except under ElevenLabs realtime, which can't take a recorded clip: then the
    /// Push-to-Talk engine serves (FR-017).
    /// </summary>
    public DictationTurnEngine ResolveEngine(DictationCaptureMode mode) =>
        mode == DictationCaptureMode.PushToTalk && PrimaryEngine == DictationPrimaryEngine.ElevenLabsRealtime
            ? ToTurnEngine(PushToTalkEngine)
            : ToTurnEngine(PrimaryEngine);

    /// <summary>True when <paramref name="engine"/> runs on the vendor whose subscription lapsed. Other engines keep serving.</summary>
    public bool IsSuspendedFor(DictationTurnEngine engine)
    {
        if (State != DictationEngineState.Suspended || SuspendedEngine is not { } suspended)
        {
            return false;
        }

        var vendor = VendorOf(engine);
        return vendor is not null && vendor == VendorOf(ToTurnEngine(suspended));
    }

    /// <summary>
    /// Suspends a cloud engine after a subscription-type failure (FR-016). Returns whether the
    /// state changed, so only the first failure is logged as the suspension. A failure from an
    /// engine no longer in use (a request that was in flight when an administrator changed the
    /// choice) changes nothing.
    /// </summary>
    public bool Suspend(DictationPrimaryEngine engine, string sanitizedReason, DateTime utcNow)
    {
        if (engine == DictationPrimaryEngine.LocalWhisper)
        {
            throw new DomainRuleViolationException("Local Whisper failures never suspend dictation.");
        }

        if (State == DictationEngineState.Suspended || !IsInUse(engine))
        {
            return false;
        }

        var reason = string.IsNullOrWhiteSpace(sanitizedReason) ? "The engine refused the request." : sanitizedReason.Trim();

        State = DictationEngineState.Suspended;
        SuspendedEngine = engine;
        SuspendedAtUtc = utcNow;
        SuspensionReason = reason.Length > MaxSuspensionReasonLength ? reason[..MaxSuspensionReasonLength] : reason;
        Touch(actor: null, utcNow);
        return true;
    }

    /// <summary>
    /// A vendor was switched off under Admin → AI providers (FR-015): every choice that uses it,
    /// the primary engine and/or the Push-to-Talk engine, goes back to Local Whisper, and a
    /// suspension of that vendor is cleared. Returns whether anything changed.
    /// </summary>
    public bool RevertToLocalWhisper(string providerKey, DictationRevertReason reason, DateTime utcNow)
    {
        var vendor = Normalize(providerKey);
        if (vendor is null || !DependsOnVendor(vendor))
        {
            return false;
        }

        DictationPrimaryEngine? revertedFrom = null;

        if (VendorOf(ToTurnEngine(PrimaryEngine)) == vendor)
        {
            revertedFrom = PrimaryEngine;
            PrimaryEngine = DictationPrimaryEngine.LocalWhisper;
        }

        if (VendorOf(ToTurnEngine(PushToTalkEngine)) == vendor)
        {
            revertedFrom ??= DictationPrimaryEngine.OpenAiWhisper;
            PushToTalkEngine = DictationClipEngine.LocalWhisper;
        }

        if (SuspendedEngine is { } suspended && VendorOf(ToTurnEngine(suspended)) == vendor)
        {
            ClearSuspension();
        }

        LastRevertedAtUtc = utcNow;
        LastRevertReason = reason;
        LastRevertedFrom = revertedFrom;
        Touch(actor: null, utcNow);
        return true;
    }

    /// <summary>True when the primary engine or the Push-to-Talk engine runs on the vendor.</summary>
    public bool DependsOnVendor(string providerKey)
    {
        var vendor = Normalize(providerKey);
        return vendor is not null
            && (VendorOf(ToTurnEngine(PrimaryEngine)) == vendor || VendorOf(ToTurnEngine(PushToTalkEngine)) == vendor);
    }

    /// <summary>The <c>AIProvider.ProviderKey</c> an engine runs on, or null for Local Whisper and the browser built-in.</summary>
    public static string? VendorOf(DictationTurnEngine engine) => engine switch
    {
        DictationTurnEngine.OpenAiWhisper => OpenAiVendorKey,
        DictationTurnEngine.ElevenLabsRealtime => ElevenLabsVendorKey,
        _ => null,
    };

    public static DictationTurnEngine ToTurnEngine(DictationPrimaryEngine engine) => engine switch
    {
        DictationPrimaryEngine.OpenAiWhisper => DictationTurnEngine.OpenAiWhisper,
        DictationPrimaryEngine.ElevenLabsRealtime => DictationTurnEngine.ElevenLabsRealtime,
        _ => DictationTurnEngine.LocalWhisper,
    };

    public static DictationTurnEngine ToTurnEngine(DictationClipEngine engine) => engine switch
    {
        DictationClipEngine.OpenAiWhisper => DictationTurnEngine.OpenAiWhisper,
        DictationClipEngine.Browser => DictationTurnEngine.Browser,
        _ => DictationTurnEngine.LocalWhisper,
    };

    /// <summary>In use for some turn right now: the primary engine, or the Push-to-Talk engine under ElevenLabs realtime.</summary>
    private bool IsInUse(DictationPrimaryEngine engine) =>
        engine == PrimaryEngine
        || (PrimaryEngine == DictationPrimaryEngine.ElevenLabsRealtime
            && ToTurnEngine(PushToTalkEngine) == ToTurnEngine(engine));

    private void ClearSuspension()
    {
        State = DictationEngineState.Active;
        SuspendedEngine = null;
        SuspendedAtUtc = null;
        SuspensionReason = null;
    }

    private void Touch(string? actor, DateTime utcNow)
    {
        ModifiedAtUtc = utcNow;
        ModifiedBy = actor;
    }

    private static string? Normalize(string? providerKey) =>
        string.IsNullOrWhiteSpace(providerKey) ? null : providerKey.Trim().ToLowerInvariant();
}
