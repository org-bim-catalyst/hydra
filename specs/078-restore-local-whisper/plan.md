# Implementation Plan: Restore Local Whisper as the Primary Dictation Engine

**Branch**: `main` (solo, direct-to-main) | **Date**: 2026-09-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/078-restore-local-whisper/spec.md`

**Note**: Planned by hand in this directory. `.specify/feature.json` points at another session's
feature (079) and is deliberately left untouched, so the speckit PowerShell setup scripts were
not run.

## Summary

Bring back the self-hosted **Local Whisper** engine (Whisper.net, in-process, removed in `2c1717be`)
as the platform's default dictation engine. The engine loads lazily rather than at startup. No
model ships or downloads: a fresh deployment dictates through the **Browser built-in** until an
admin deploys a model (the recommended multilingual `ggml-base.bin`) through Custom Models and
selects it.

Add a platform-wide **DictationEngineSetting**. Admins choose the primary engine (Local Whisper /
OpenAI Whisper / ElevenLabs realtime), the Push-to-Talk engine used while ElevenLabs realtime is
primary (Local Whisper by default / OpenAI Whisper / Browser built-in), and the Local Whisper model,
and can try a model before selecting it.

**Failure policy**: any failure falls only to the **Browser built-in**, never to another engine. A
subscription-type failure of a selected cloud engine suspends it and puts every path that uses
it on the browser built-in until an admin acts. Switching a vendor off reverts any choice that
uses it to Local Whisper.

**Capture**: both capture paths ask `stt-session` first, which doubles as the health pre-check.
Clips are converted to 16 kHz mono WAV in the browser and posted to a new dictation endpoint that
routes to the Local Whisper or OpenAI Whisper clip transcriber.

**Custom Models**: deploys only the file a `/resolve/` or `/blob/` URL names.

Every failure is recorded on the specs/074 trail.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 5 + React 19 (frontend)

**Primary Dependencies**:
- Existing: ASP.NET Core, MediatR, FluentValidation, EF Core 10, MUI, TanStack Query.
- **Restored**: `Whisper.net` + `Whisper.net.Runtime` 1.9.1 (research D1; no native DLL name
  collision).
- Browser: Web Audio API (`OfflineAudioContext`) for WAV conversion, with no new npm dependency.

**Storage**:
- SQL Server: a new `DictationEngineSettings` table and a new `CustomModels.SourceFilePath` column,
  added in one migration.
- Model files on the server filesystem, only under Custom Models destinations. The loose
  `App_Data/whisper-models/ggml-BaseEn.bin` is left alone and never read.

**Testing**:
- xUnit v3 + NSubstitute (Domain, Application, Infrastructure, Web).
- Persistence tests only against test2.
- Vitest + Testing Library + axe (frontend).

**Target Platform**: Windows shared host (site4now), IIS in-process. Evergreen desktop and mobile
browsers.

**Project Type**: Web application (ASP.NET Core API + React SPA in `ClientApp`)

**Performance Goals**:
- Pre-check (`stt-session`) adds no network call for Local Whisper or OpenAI Whisper.
- Local Whisper: a 10 s clip transcribed in ≤ 5 s p95 on `ggml-base.bin` (measured via Try it,
  not a build gate).
- A single-file deploy transfers only that file (SC-005).

**Constraints**:
- No model load or download at startup.
- At most 2 concurrent Local Whisper transcriptions (configurable).
- Clip upload ≤ 4 MB (60 s WAV ≈ 1.9 MB).
- No ffmpeg on the host.
- `ggml-BaseEn.bin` must never be deleted by code.
- No `ValidateOnStart`.
- The Application layer never references EF Core.

**Scale/Scope**: Platform-wide single setting; tens of concurrent dictating users; 5 languages
(en/ar/es/fr/de).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| §2.I Clean Architecture / §3 dependency rule | ✅ | `DictationEngineSetting` + enums in Domain. Ports (`IDictationClipTranscriber`, `IDictationEngineSettingRepository`, `ILocalWhisperModelCatalog`, `IAiProviderSwitchedOffObserver`) in Application. Whisper.net, HTTP and EF only in Infrastructure/Persistence. Controllers only send MediatR requests. |
| §2.II SOLID / ISP | ✅ | One transcriber per engine behind one port. Local Whisper does not implement `IHostedModelEngine`, whose repository-id model doesn't fit. |
| §2.III Simplicity | ✅ | Reuses stt-session, `IVoiceFailureReporter`, `IFailureClassifier` + `IsCritical`, `IHostedModelLocator`, `pcm16.ts`. No notification subsystem, no domain-event dispatcher (see Complexity Tracking). |
| §2.V Testability | ✅ | Every branch of the stt-session decision table and the clip handler is unit-testable with fakes. The Whisper.net wrapper sits behind the port. |
| §2.VIII No Silent Failures | ✅ | Every failure path is caught, reported on the trail (FR-007) and surfaced as the gentle repeat. A browser-side decode failure uploads the raw clip so the server records it. A selected model whose file is missing is a failure on the trail and is shown as `effectiveModel.problem` on the admin page; "no model selected" is shown there too but is not a failure. The admin Try it surfaces its reason. No fire-and-forget promises in the hooks. |
| §3 Domain events | ⚠️ justified | FR-015's reaction to a vendor switch-off uses an observer interface in the same unit of work, not a domain event, because the codebase has no dispatcher. See Complexity Tracking. |
| §5 Database | ✅ | Code-first migration, Fluent API config, concurrency token on the singleton, no destructive change (new nullable column). |
| §6 API | ✅ | Versioned `/api/v1` routes, Problem Details with typed `type` values, 409 on concurrency, 422 on validation. `stt-session` stays backward compatible for old tabs (`mode` optional; old `/ai/transcriptions` unchanged). |
| §7 UI | ✅ | MUI section on the existing Admin → Voice page with axe tests. Gentle-repeat copy in the 5 supported languages (a small phrase map; the app has no i18n framework, and admin copy stays English like the rest of the admin UI). Voice output uses the persona voice (CLAUDE.md voice rule). |
| §8 Security | ✅ | Admin routes use the Administrator policy. Uploads are size-limited and header-validated, not content-type-trusted. No vendor detail to end users. The HF URL is never fetched as given (specs/072 SSRF layers unchanged). The single-file path goes through `destination.TryCombine`. |
| §9 AI — provider abstraction, fallback policy | ✅ | The dictation fallback policy (browser only) is defined once in the stt-session/clip handlers, not per call site. The vendor is chosen by configuration (the admin setting). |
| §10 Testing | ✅ | Unit, integration (Infrastructure WAV/ggml/deploy filter; Persistence on test2), Web route tests, vitest + axe. Tests ship in the same commits. |
| §13 Documentation | ✅ | Updates specs/012 contract + research "Superseded" note, ADR 0006, specs/070 spec note, specs/072 contracts + ADR 0016, plus a new ADR for the dictation engine policy. |
| §14 Observability | ✅ | `[LoggerMessage]` for model load/swap, suspension and revert. Correlation id on every trail entry. |

**Gate result**: PASS (one justified deviation).

## Project Structure

### Documentation (this feature)

```text
specs/078-restore-local-whisper/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D14
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── dictation-session.md          # stt-session: Realtime | Clip | Browser, + mode
│   ├── dictation-transcription.md    # POST /ai/voice/transcriptions
│   ├── admin-dictation.md            # admin get/set primary, select model, try, guards
│   └── custom-models-single-file.md  # filePath rename + single-file deploy
├── checklists/requirements.md
└── tasks.md             # Next: /speckit-tasks
```

### Source Code (repository root)

```text
src/AskLucy.Domain/
├── Ai/Dictation/                       # NEW DictationEngineSetting, DictationPrimaryEngine, DictationClipEngine,
│                                       #     DictationTurnEngine, DictationEngineState, DictationRevertReason
└── CustomModels/
    ├── CustomModel.cs                  # + SourceFilePath
    └── HuggingFaceModelSource.cs       # IgnoredFilePath → FilePath

src/AskLucy.Application/
├── Abstractions/                       # NEW IDictationClipTranscriber, ILocalWhisperModelCatalog,
│                                       #     IDictationEngineSettingRepository, IAiProviderSwitchedOffObserver
├── Ai/
│   ├── Commands/CreateSpeechToTextSession/   # decision table (research D6); DictationEngine → Realtime|Clip|Browser
│   ├── Commands/TranscribeDictationClip/     # NEW command + validator + handler
│   ├── Commands/UpdateAiProvider/            # notify switch-off observers (FR-015)
│   ├── Dictation/                      # NEW DictationFailurePolicy (report + suspend), switch-off observer,
│   │   ├── Commands/SetDictationPrimaryEngine/
│   │   ├── Commands/SetPushToTalkEngine/
│   │   ├── Commands/SelectLocalWhisperModel/
│   │   ├── Commands/TryLocalWhisperModel/
│   │   └── Queries/GetDictationSettings/
│   └── VoiceFailureReporter.cs         # (existing; reused as-is)
└── CustomModels/
    ├── Abstractions/IHostedModelLocator.cs  # + ResolveByIdAsync
    ├── Jobs/CustomModelDeploymentJob.cs     # single-file listing filter (FR-011)
    ├── Commands/RemoveCustomModel/          # FR-009b guard
    ├── Commands/SubmitCustomModelDeployment/ # persist SourceFilePath
    └── CustomModelDtos.cs, CustomModelSummaryBuilder.cs  # filePath, selectedForLocalWhisper

src/AskLucy.Infrastructure/
├── Ai/LocalWhisper/                    # NEW LocalWhisperOptions, LocalWhisperTranscriber (factory cache + lease),
│                                       #     LocalWhisperModelCatalog (ggml magic), WavHeader
├── Ai/OpenAiWhisperClipTranscriber.cs  # NEW adapter over OpenAIProvider
├── CustomModels/ScopedHostedModelLocator.cs  # + ResolveByIdAsync
├── AskLucy.Infrastructure.csproj       # + Whisper.net, Whisper.net.Runtime 1.9.1
└── DependencyInjection.cs              # registrations

src/AskLucy.Persistence/
├── Configurations/DictationEngineSettingConfiguration.cs   # NEW
├── Configurations/CustomModelConfiguration.cs              # + SourceFilePath
├── Repositories/DictationEngineSettingRepository.cs        # NEW
└── Migrations/<ts>_RestoreLocalWhisperDictation.cs         # NEW (no BOM)

src/AskLucy.Web/
├── Controllers/v1/AiController.cs                  # stt-session mode; POST voice/transcriptions
├── Controllers/v1/AdminVoiceProvidersController.cs # dictation routes
├── Controllers/v1/AdminCustomModelsController.cs   # 409 mapping for the removal guard
├── appsettings.json                                # LocalWhisper section (user's uncommitted Smtp edit left alone)
└── ClientApp/src/
    ├── features/chat/api/voiceApi.ts               # engine union, mode, degraded, transcribeDictationClip
    ├── features/chat/voice/wavEncoder.ts           # NEW (reuses pcm16.ts)
    ├── features/chat/voice/dictationFallback.ts    # clip engine posts WAV to the new endpoint
    ├── features/chat/voice/useSpeechRecognition.ts # Clip/Browser handling, gentle repeat
    ├── features/chat/voice/useVoiceRecorder.ts     # pre-check, WAV, gentle repeat
    ├── features/chat/pages/ChatPage.tsx            # wire gentle-repeat speech via useVoiceOutput
    ├── features/admin/api/adminVoiceApi.ts         # dictation endpoints
    ├── features/admin/pages/AdminVoicePage.tsx     # Dictation section (+ Try it recorder)
    ├── features/admin/components/DictationSettingsSection.tsx  # NEW
    ├── features/admin/components/customModels/     # filePath notice, Local Whisper chip, remove guard
    ├── features/admin/adminNav.tsx                 # Suspended badge on Voice
    └── features/chat/voice/gentleRepeat.ts         # NEW en/ar/es/fr/de phrase map (no i18n framework exists)

tests/
├── AskLucy.Domain.Tests/Ai/Dictation/
├── AskLucy.Application.Tests/Ai/ (stt-session decision table, clip handler, admin commands, switch-off observer)
├── AskLucy.Application.Tests/CustomModels/ (single-file filter, removal guard)
├── AskLucy.Infrastructure.Tests/Ai/LocalWhisper/ (WavHeader, ggml magic, model catalog)
├── AskLucy.Persistence.Tests/ (singleton row, SourceFilePath)   # test2 only
└── AskLucy.Web.Tests/ (routes, limits, Problem Details)

docs/adr/0018-dictation-engine-policy.md   # NEW
```

**Structure Decision**: The existing Clean Architecture web-application layout. Dictation lives
beside the existing voice code in `Application/Ai`, `Infrastructure/Ai` and `ClientApp/features/chat/voice`.
The admin UI extends Admin → Voice rather than adding a page.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| FR-015 reaction via `IAiProviderSwitchedOffObserver` instead of a domain event (§3 "Domain events") | The provider row and the dictation setting must change in one transaction, and the AI Provider engine must not reference the dictation module. | A domain-event dispatcher does not exist. Building one (collect on aggregates, dispatch after commit) for one reaction is disproportionate, and dispatch-after-commit would reintroduce the two-transaction inconsistency the observer avoids. |

## Post-Design Constitution Re-check

Re-evaluated after data-model and contracts: no new violations.
- The singleton aggregate carries its invariants (suspend/revert/clear) in Domain.
- Handlers only orchestrate.
- The failure trail and the classifier are reused, not duplicated.
- The stt-session change is additive for old clients (`mode` optional, legacy upload route unchanged).

**Gate: PASS.**

## Open Points for the User (carried into tasks)

1. **Completed Custom Models cannot be removed today** (`CustomModel.Remove` allows only
   Failed/Cancelled, and leaves files on the target). "Delete the old one later" needs a specs/072
   extension. This feature only adds the FR-009b guard. `ggml-BaseEn.bin` is a loose file, retired
   by a manual FTP delete after FR-014's confirmation.
2. **Admin notification is in-app only**: a Critical incident on the trail, the Suspended banner and
   the admin nav badge. Email waits for specs/067 via the existing `CriticalIncidentOpened` seam.
3. **Q5 interpretation**: a deliberate vendor switch-off *reverts* to Local Whisper (FR-015); a
   subscription failure *suspends* that engine to the browser built-in (FR-016).
4. **Resolved 2026-09-27**: Push-to-Talk under ElevenLabs realtime uses the Push-to-Talk engine,
   Local Whisper by default (FR-017); no ElevenLabs batch call. A fresh deployment uses the
   browser built-in until an admin deploys and selects a model (FR-002); nothing auto-downloads.
5. **Behavior change on deploy**: production dictation today goes to OpenAI Whisper for clips.
   After this deploy it goes to the browser built-in until an admin deploys and selects
   `ggml-base.bin` (or sets OpenAI Whisper as primary). Switching a vendor on never selects it.
