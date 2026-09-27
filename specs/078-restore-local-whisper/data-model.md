# Data Model: Restore Local Whisper as the Primary Dictation Engine

**Feature**: specs/078-restore-local-whisper | **Date**: 2026-09-27

One new aggregate, one new column on an existing aggregate, and one migration
(`RestoreLocalWhisperDictation`). No data is backfilled: the setting row is created on first read
with the defaults below, and existing Custom Models deployments get `SourceFilePath = NULL`, which
means "whole repository" (today's behavior).

---

## New aggregate: `DictationEngineSetting` (Domain, `AskLucy.Domain.Ai.Dictation`)

The platform-wide dictation choice (spec Key Entities: "Dictation engine setting" and "Local
Whisper model selection"). It is a singleton: there is exactly one row, with a fixed key.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Fixed well-known value (`DictationEngineSetting.SingletonId`); a unique constraint guarantees one row. |
| `PrimaryEngine` | `DictationPrimaryEngine` (enum, stored as string) | `LocalWhisper` (default) / `OpenAiWhisper` / `ElevenLabsRealtime`. |
| `PushToTalkEngine` | `DictationClipEngine` (enum, string) | `LocalWhisper` (default) / `OpenAiWhisper` / `Browser`. Used only while `PrimaryEngine = ElevenLabsRealtime` (FR-017). |
| `LocalWhisperModelId` | `Guid?` | `null` = none selected (a fresh deployment): Local Whisper paths use the browser built-in as the normal path (FR-002/FR-010). Otherwise a `CustomModels.Id`. No FK cascade: removal is refused by the application guard (FR-009b), and an Unavailable model resolves to the browser built-in at run time (FR-010). |
| `State` | `DictationEngineState` (enum, string) | `Active` / `Suspended`. |
| `SuspendedAtUtc` | `DateTime?` | Set only while `Suspended`. |
| `SuspensionReason` | `string?` (max 500) | A sanitized reason (`FailureReasonSanitizer`), shown to admins. Set only while `Suspended`. |
| `SuspendedEngine` | `DictationPrimaryEngine?` | The engine that lapsed. Only paths using it go to the browser built-in (FR-016); the banner names it. |
| `LastRevertedAtUtc` | `DateTime?` | FR-015. |
| `LastRevertReason` | `DictationRevertReason?` (enum, string) | `VendorSwitchedOff`. |
| `LastRevertedFrom` | `DictationPrimaryEngine?` | The engine that was reverted (primary or Push-to-Talk engine). |
| `UpdatedAtUtc` | `DateTime` | |
| `UpdatedBy` | `string?` (max 450) | Admin user id, or `null` for a system change (revert or suspension). |
| `RowVersion` | `byte[]` | Concurrency token: two admins saving at once gets a 409 rather than a silent overwrite. |

### Domain methods (invariants live here, not in handlers)

- `static CreateDefault(utcNow)` → primary `LocalWhisper`, Push-to-Talk engine `LocalWhisper`, no
  model selected, `Active`.
- `SetPrimary(engine, actor, utcNow)` — any engine (the handler has already checked FR-004,
  "vendor switched on"). **Clears a suspension** (FR-016: "until an administrator sets a primary
  engine again … which clears the suspension"), even when the same engine is set again after a
  renewal.
- `SetPushToTalkEngine(DictationClipEngine engine, actor, utcNow)` — the handler has already
  checked OpenAI is switched on for `OpenAiWhisper` (FR-017). Clears a suspension of that engine's
  vendor.
- `SelectLocalWhisperModel(Guid? customModelId, actor, utcNow)` — `null` = none (browser built-in
  serves). The handler has already checked the model is selectable (research D12).
- `ResolveEngine(DictationCaptureMode mode)` → `DictationTurnEngine` (`LocalWhisper` /
  `OpenAiWhisper` / `ElevenLabsRealtime` / `Browser`): Continuous → primary; Push-to-Talk → primary,
  or `PushToTalkEngine` while the primary is `ElevenLabsRealtime` (research D4/D6).
- `IsSuspendedFor(DictationTurnEngine engine)` — `Suspended` and `engine` uses `SuspendedEngine`'s
  vendor.
- `Suspend(engine, sanitizedReason, utcNow)` — no-op when already `Suspended`; refused for
  `LocalWhisper` (Local Whisper failures never suspend, research D8); refused unless `engine` is
  still selected as the primary or the Push-to-Talk engine (a stale in-flight failure from a
  previous choice must not suspend the new one). Returns whether the state changed, so the first
  failure only is logged as the suspension.
- `RevertToLocalWhisper(string providerKey, DictationRevertReason reason, utcNow)` — resets the
  primary and/or the Push-to-Talk engine that depend on the switched-off vendor to `LocalWhisper`;
  sets the `LastReverted*` fields and clears a suspension of that vendor. Returns whether anything
  changed.
- `DependsOnVendor(string providerKey)` — true when the primary or the Push-to-Talk engine uses
  it: `OpenAiWhisper` ↔ OpenAI, `ElevenLabsRealtime` ↔ ElevenLabs, `LocalWhisper`/`Browser` ↔ none.
- `IsSelected(Guid customModelId)`.

### State transitions

```text
            SetPrimary / SetPushToTalkEngine / RevertToLocalWhisper
        ┌──────────────────────────────────────────────────────┐
        ▼                                                      │
     Active ── Suspend(selected cloud engine, Critical kind) ──► Suspended
```

While `Suspended`, the stt-session answers `Browser` for every path that would use the suspended
vendor, and no dictation request reaches it (FR-016, SC-006). Paths using another engine (for
example Local Whisper as the Push-to-Talk engine under an ElevenLabs suspension) are unaffected.

### Repository (Application port, `IDictationEngineSettingRepository`)

- `Task<DictationEngineSetting> GetOrCreateAsync(CancellationToken)` — creates the default row on
  first call; an insert race is resolved by re-reading on a unique-key conflict.
- Changes are committed through the request's `IUnitOfWork`. A suspension raised during a
  dictation request is committed by that request's handler.

### Persistence (`AskLucy.Persistence`)

- Table `DictationEngineSettings`; `DictationEngineSettingConfiguration` (Fluent API only, no
  attributes on the Domain class). Enums are stored as `nvarchar(32)` strings, and `RowVersion` is
  `IsRowVersion()`.

---

## Changed aggregate: `CustomModel` (specs/072)

| Field | Change |
|---|---|
| `SourceFilePath` | **New**, `string?`, max 1024 (the same limit as other repository-relative paths in the deployment job). Set from the source URL's `/resolve/<rev>/<file>` or `/blob/<rev>/<file>` part (FR-011). `null` = whole repository (unchanged behavior). Immutable after submit. |

`HuggingFaceModelSource.IgnoredFilePath` is renamed `FilePath`. The DTO field
`SourcePreviewDto.IgnoredFilePath` / `SubmittedCustomModelDto.IgnoredFilePath` becomes `filePath`
(contracts/custom-models-single-file.md). `CustomModelSummaryDto` gains `sourceFilePath` and
`selectedForLocalWhisper` (read from the setting).

`Remove(...)` is **unchanged** (Failed/Cancelled only). `RemoveCustomModelCommandHandler` adds the
FR-009b guard, so the rule already holds if removal of Completed models is ever allowed
(research D12 gap).

---

## Value types (Domain)

- `enum DictationPrimaryEngine { LocalWhisper, OpenAiWhisper, ElevenLabsRealtime }`
- `enum DictationClipEngine { LocalWhisper, OpenAiWhisper, Browser }` — the Push-to-Talk engine
  choices (FR-017).
- `enum DictationTurnEngine { LocalWhisper, OpenAiWhisper, ElevenLabsRealtime, Browser }` — what
  `ResolveEngine` returns.
- `enum DictationEngineState { Active, Suspended }`
- `enum DictationRevertReason { VendorSwitchedOff }`

## Application-only types

- `enum DictationEngine { Realtime, Clip, Browser }` — the stt-session answer (renames today's
  `Whisper` to `Clip`, research D6).
- `enum DictationCaptureMode { Continuous, PushToTalk }`.
- `record DictationTranscript(string Text, string? DetectedLanguage, TimeSpan Elapsed)`.
- `interface IDictationClipTranscriber` (research D4).
- `interface ILocalWhisperModelCatalog` — resolves the selected model file (None / Unavailable /
  Ready(path) / Broken(reason)) and answers "is this deployment selectable?" (the ggml magic
  check).
- `interface IAiProviderSwitchedOffObserver` (research D10).
