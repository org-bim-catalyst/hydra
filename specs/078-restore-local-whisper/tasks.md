---

description: "Task list for restoring Local Whisper as the primary dictation engine"
---

# Tasks: Restore Local Whisper as the Primary Dictation Engine

**Input**: Design documents from `specs/078-restore-local-whisper/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. The constitution (§10) requires unit, integration and UI tests, shipped in the same commits.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

**Parallel-session rule**: `.specify/feature.json` belongs to another session (079). Do not modify it,
and do not run the speckit PowerShell scripts. Always use explicit paths under
`specs/078-restore-local-whisper/`.

Engine names used throughout: **Local Whisper**, **OpenAI Whisper**, **ElevenLabs realtime**, **Browser built-in**.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Every task names its exact file path(s)

## Path Conventions

- Backend: `src/AskLucy.{Domain,Application,Infrastructure,Persistence,Web}/`
- Frontend: `src/AskLucy.Web/ClientApp/src/`
- Tests: `tests/AskLucy.{Domain,Application,Infrastructure,Persistence,Web}.Tests/`

## Story order

All four stories are P1. They are ordered by dependency rather than by number:
- **US4** (deploy and select a model) comes before **US1** (serve dictation on it). With nothing
  auto-downloaded, Local Whisper cannot serve until a model is deployed and selected.
- **US2** (failures) and **US3** (admin engine choice) build on the US1 dictation path.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Restore the Local Whisper runtime dependency and its configuration.

- [ ] T001 Restore `Whisper.net` and `Whisper.net.Runtime` 1.9.1 (the exact versions removed in `2c1717be`; see `git show 2c1717be -- src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj`) in `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj`. Then confirm no other package ships `whisper.dll` or `ggml*.dll` (memory: PDFium native DLL collision) by listing `src/AskLucy.Web/bin/**/runtimes/**` after a build.
- [X] T002 [P] Create `src/AskLucy.Infrastructure/Ai/LocalWhisper/LocalWhisperOptions.cs` (section `LocalWhisper`) with `MaxConcurrentTranscriptions` (default 2) and `QueueTimeoutSeconds` (default 10). No `DefaultModelFile`; no `ValidateOnStart`; defaults in code so a missing section never fails the host (memory: required IOptions crash).
- [X] T003 [P] Add a `LocalWhisper` section with the two defaults to `src/AskLucy.Web/appsettings.json`. Edit only that section; the user's uncommitted Smtp change in the same file must stay uncommitted (stage with `git add -p`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The setting aggregate, its persistence, the Application ports, the Local Whisper runtime and the browser WAV encoder. Every story uses them.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for the foundation

- [X] T004 [P] Domain tests for `DictationEngineSetting` in `tests/AskLucy.Domain.Tests/Ai/Dictation/DictationEngineSettingTests.cs`, covering:
  - `CreateDefault`: primary and Push-to-Talk engine both `LocalWhisper`, no model, `Active`.
  - `SetPrimary`: clears a suspension, including when the same engine is set again.
  - `SetPushToTalkEngine`: clears a suspension of that engine's vendor.
  - `SelectLocalWhisperModel(null)`.
  - `ResolveEngine`: Continuous → primary. Push-to-Talk → primary, except `PushToTalkEngine` while the primary is `ElevenLabsRealtime`.
  - `Suspend`: refused for `LocalWhisper` and for an engine no longer selected; a no-op when already suspended; returns whether the state changed.
  - `IsSuspendedFor`: Local Whisper as the Push-to-Talk engine is not suspended by an ElevenLabs suspension.
  - `RevertToLocalWhisper`: resets primary and/or Push-to-Talk engine for OpenAI; resets primary for ElevenLabs; is a no-op for an unrelated vendor; sets the `LastReverted*` fields; clears that vendor's suspension.
  - `DependsOnVendor`.
- [X] T005 [P] Infrastructure tests for the WAV header reader in `tests/AskLucy.Infrastructure.Tests/Ai/LocalWhisper/WavHeaderTests.cs`: _(Done: the WAV header tests live in `tests/AskLucy.Application.Tests/Ai/Dictation/`, because the reader is used by the Application try handler too.)_
  - Accepts RIFF/WAVE PCM(1), 1 channel, 16000 Hz, 16-bit.
  - Rejects other rates, stereo, float, truncated headers, webm/ogg bytes and an empty stream.
- [X] T006 [P] Infrastructure tests for the model catalog in `tests/AskLucy.Infrastructure.Tests/Ai/LocalWhisper/LocalWhisperModelCatalogTests.cs`, using a temp directory:
  - `None` when nothing is selected.
  - `Unavailable` for an Unavailable deployment.
  - `Ready(path)` for a Completed deployment whose `<destination>/<SourceFilePath>` starts with the ggml magic `lmgg`.
  - `Broken(reason)` for a missing file or wrong magic.
  - The selectable check refuses a deployment with no `SourceFilePath` ("Deploy the model from a URL that names its .bin file") and one that isn't Completed.
- [X] T007 [P] Infrastructure tests for `LocalWhisperTranscriber` in `tests/AskLucy.Infrastructure.Tests/Ai/LocalWhisper/LocalWhisperTranscriberTests.cs`, behind a factory seam with no real model:
  - A factory is loaded lazily on the first clip and cached per model path.
  - A model change loads the new model; the old factory is disposed only after its lease count reaches zero.
  - The concurrency cap: a request that can't get a slot within `QueueTimeoutSeconds` throws an exception the classifier maps to `Unavailable`.
- [X] T008 [P] Persistence test for the singleton row and `CustomModels.SourceFilePath` round-trip in `tests/AskLucy.Persistence.Tests/Dictation/DictationEngineSettingRepositoryTests.cs`. Cover `GetOrCreateAsync` creating the row once, and resolving a concurrent insert by re-reading. Run against test2 only (`PERSISTENCE_TESTS_2_CONNECTION_STRING`).
- [X] T009 [P] Frontend test for the WAV encoder in `src/AskLucy.Web/ClientApp/src/features/chat/voice/wavEncoder.test.ts`:
  - The output starts with a 44-byte RIFF header: PCM, 1 channel, 16000 Hz, 16-bit, correct data length.
  - A decode failure resolves to the raw blob with a `converted: false` flag rather than throwing.

### Implementation for the foundation

- [X] T010 [P] Create the Domain enums in `src/AskLucy.Domain/Ai/Dictation/`: `DictationPrimaryEngine.cs`, `DictationClipEngine.cs`, `DictationTurnEngine.cs`, `DictationEngineState.cs` and `DictationRevertReason.cs` (data-model.md "Value types").
- [X] T011 Create the aggregate `src/AskLucy.Domain/Ai/Dictation/DictationEngineSetting.cs`:
  - `SingletonId`, every field in data-model.md, and every method listed there.
  - All invariants live in the aggregate, none in handlers.
  - Make T004 pass.
- [X] T012 [P] Add `SourceFilePath` (nullable, max 1024, immutable after submit) to `src/AskLucy.Domain/CustomModels/CustomModel.cs`, set through its create factory.
- [X] T013 [P] Create the Application ports in `src/AskLucy.Application/Abstractions/`:
  - `IDictationEngineSettingRepository.cs` (`GetOrCreateAsync`).
  - `IDictationClipTranscriber.cs` (`DictationClipEngine Engine`; `TranscribeAsync(Stream wav, string? language, CancellationToken)` → `DictationTranscript`).
  - `ILocalWhisperModelCatalog.cs` (`ResolveSelectedAsync(Guid? id)` → None/Unavailable/Ready/Broken; `CheckSelectableAsync(Guid id)`).
  - `IAiProviderSwitchedOffObserver.cs`.
  - Put `DictationTranscript` and `DictationCaptureMode` in `src/AskLucy.Application/Ai/Dictation/DictationTypes.cs`.
  - No EF Core reference (memory: Application never references EF Core).
- [X] T014 [P] Create `src/AskLucy.Persistence/Configurations/DictationEngineSettingConfiguration.cs`: table `DictationEngineSettings`, enums as `nvarchar(32)` strings, `RowVersion` as `IsRowVersion()`, `SuspensionReason` max 500, `UpdatedBy` max 450, unique key on `Id`. Also map `SourceFilePath` (max 1024) in `src/AskLucy.Persistence/Configurations/CustomModelConfiguration.cs`.
- [X] T015 Add the `DbSet<DictationEngineSetting>` to `src/AskLucy.Persistence/AskLucyDbContext.cs`. Create `src/AskLucy.Persistence/Repositories/DictationEngineSettingRepository.cs`: create on first read, and re-read on a unique-key conflict. Register it in `src/AskLucy.Persistence/DependencyInjection.cs`.
- [X] T016 Generate the migration `RestoreLocalWhisperDictation` in `src/AskLucy.Persistence/Migrations/`:
  - It adds the new table plus the nullable `CustomModels.SourceFilePath` column; no backfill.
  - Strip any BOM, and check line endings (memory: dotnet format & migration CI gotchas).
  - Apply it by hand to test2 (`db_a15752_asklucytest2`) only, never the shared test DB. Make T008 pass.
- [X] T017 [P] Create `src/AskLucy.Infrastructure/Ai/LocalWhisper/WavHeader.cs` (header reader and validator; make T005 pass).
- [X] T018 [P] Create `src/AskLucy.Infrastructure/Ai/LocalWhisper/LocalWhisperModelCatalog.cs` implementing `ILocalWhisperModelCatalog`:
  - Resolve the deployment via `IHostedModelLocator.ResolveByIdAsync` (added in T020), combine `<destination>/<SourceFilePath>` through the existing safe path combination, and read the first 4 bytes for the ggml magic.
  - Never read the loose `App_Data/whisper-models/ggml-BaseEn.bin`.
  - Make T006 pass.
- [X] T019 Create `src/AskLucy.Infrastructure/Ai/LocalWhisper/LocalWhisperTranscriber.cs` implementing `IDictationClipTranscriber` (Engine = `LocalWhisper`), per research D1:
  - Load lazily; cache one factory per model path, with a lease count.
  - Cap concurrency with a `SemaphoreSlim` sized from the options.
  - Pass the language hint to `WithLanguage(...)`, otherwise use `WithLanguageDetection()`.
  - Log model load and swap with `[LoggerMessage]`.
  - Use the removed `WhisperLocalTranscriptionProvider.cs` (`git show 2c1717be^:src/AskLucy.Infrastructure/Ai/WhisperLocalTranscriptionProvider.cs`) as the Whisper.net API reference.
  - Make T007 pass.
- [X] T020 Add `ResolveByIdAsync(Guid customModelId)` to `src/AskLucy.Application/CustomModels/Abstractions/IHostedModelLocator.cs` and implement it in `src/AskLucy.Infrastructure/CustomModels/ScopedHostedModelLocator.cs`. It returns the same `HostedModelResolution`. Extend `tests/AskLucy.Infrastructure.Tests/CustomModels/ScopedHostedModelLocatorTests.cs` accordingly. _(N/A: `LocalWhisperModelCatalog` reads the deployment by id from `ICustomModelRepository` and combines `ContentRootPath/<Destination>/<SourceFilePath>` itself, checking containment. `HostedModelResolution` is built around a model folder, not a single named file, so `IHostedModelLocator` is left unchanged.)_
- [X] T021 Register the transcriber (singleton, owns the factory cache), the catalog (scoped) and `LocalWhisperOptions` in `src/AskLucy.Infrastructure/DependencyInjection.cs`. Use direct registrations only, never `sp => sp.GetRequiredService<T>()` aliases (memory: hidden DI cycle).
- [X] T022 [P] Create `src/AskLucy.Web/ClientApp/src/features/chat/voice/wavEncoder.ts`: `decodeAudioData` → `OfflineAudioContext(1, …, 16000)` → `float32ToInt16Pcm` from `pcm16.ts` → 44-byte RIFF header; on a decode failure return the raw blob (research D3). Make T009 pass.

**Checkpoint**: The foundation builds (`dotnet build "Ask Lucy.sln" -v q -nologo -m:1`), T004–T009 pass, and nothing is user-visible yet.

---

## Phase 3: User Story 4 - An administrator deploys and selects a Local Whisper model (Priority: P1)

**Goal**: A Hugging Face URL naming one file deploys only that file. An admin can then try a Completed ggml deployment, select it as the Local Whisper model, and can't remove it while it is selected.

**Independent Test**:
1. Add `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin`. The preview says "Only ggml-base.bin will be deployed", and the transfer total is ≈ 148 MB, not the repository's size.
2. When it completes, the Dictation section lists it as selectable, and Try it returns a transcript.
3. Select it. Removing it under Custom Models is refused.
4. Mark it Unavailable, and the Dictation section explains that the browser built-in is in use.

### Tests for User Story 4

- [X] T023 [P] [US4] Update `tests/AskLucy.Domain.Tests/CustomModels/HuggingFaceModelSourceTests.cs` for the `IgnoredFilePath` → `FilePath` rename. Cover `/resolve/`, `/blob/`, `/tree/` and a bare repository URL.
- [X] T024 [P] [US4] Extend `tests/AskLucy.Application.Tests/CustomModels/CustomModelDeploymentJobTests.cs`:
  - With `SourceFilePath` set, the listing is filtered to that one path (case-sensitive) before `PlanTransfer`, so the size cap and overwrite report see only that file, placed at `<destination>/<path>`.
  - A path absent from the listing fails the deployment with "The file <path> is not in <repo> at <revision>."
  - A null `SourceFilePath` deploys the whole repository, unchanged.
- [X] T025 [P] [US4] Extend `tests/AskLucy.Application.Tests/CustomModels/RemoveCustomModelCommandHandlerTests.cs` for the FR-009b guard: removing the selected Local Whisper model is refused with "Select a different Local Whisper model first."; other deployments are unaffected.
- [X] T026 [P] [US4] Application tests in `tests/AskLucy.Application.Tests/Ai/Dictation/LocalWhisperModelAdminTests.cs`:
  - `GetDictationSettingsQuery` lists every Completed, non-deleted deployment with its selectable reason, and reports `effectiveModel.problem` for no model selected / Unavailable / Broken.
  - `SelectLocalWhisperModelCommand` refuses an unselectable model (422 reason) and accepts `null`.
  - `TryLocalWhisperModelCommand` runs on a transient factory, never changes the setting, and returns `try-in-progress` while another try runs.
- [X] T027 [P] [US4] Web tests in `tests/AskLucy.Web.Tests/Ai/AdminDictationControllerTests.cs`, and extend `tests/AskLucy.Web.Tests/CustomModels/AdminCustomModelsControllerTests.cs`:
  - `GET /api/v1/admin/voice/dictation`, `PUT …/local-whisper-model` and `POST …/try` require Administrator.
  - A stale `rowVersion` returns 409.
  - The try route enforces the 4 MB limit.
  - The source preview returns `filePath`.
  - `DELETE /api/v1/admin/custom-models/{id}` returns 409 `custom-model-selected-for-local-whisper`.
  - Use a derived factory as the class fixture, never `WithWebHostBuilder` per test (memory).
- [X] T028 [P] [US4] Frontend tests:
  - `src/AskLucy.Web/ClientApp/src/features/admin/components/customModels/AddCustomModelDialog.test.tsx`: the "Only <file> will be deployed" notice.
  - `src/AskLucy.Web/ClientApp/src/features/admin/components/DictationSettingsSection.test.tsx`: the model picker, the no-model/Unavailable explanation, Try it recording and result, and an error toast on a failed try.

### Implementation for User Story 4

- [X] T029 [US4] Rename `IgnoredFilePath` → `FilePath` in the Domain and DTOs (contracts/custom-models-single-file.md):
  - `src/AskLucy.Domain/CustomModels/HuggingFaceModelSource.cs`.
  - `SourcePreviewDto`, `SubmittedCustomModelDto`, `SubmitCustomModelDeploymentCommand` in `src/AskLucy.Application/CustomModels/CustomModelDtos.cs` and `Commands/SubmitCustomModelDeployment/`, and `Queries/PreviewCustomModelSource/`.
  - Persist `SourceFilePath` on submit.
- [X] T030 [US4] Filter the listing to `SourceFilePath` before `PlanTransfer` in `src/AskLucy.Application/CustomModels/Jobs/CustomModelDeploymentJob.cs`, and fail with the not-in-repository reason when the path is absent (research D11). Make T024 pass.
- [X] T031 [US4] Add `sourceFilePath` and `selectedForLocalWhisper` to `CustomModelSummaryDto` in `src/AskLucy.Application/CustomModels/CustomModelDtos.cs` and `CustomModelSummaryBuilder.cs` (read the setting through `IDictationEngineSettingRepository`).
- [X] T032 [US4] Add the FR-009b guard in `src/AskLucy.Application/CustomModels/Commands/RemoveCustomModel/RemoveCustomModelCommandHandler.cs`, and map it to 409 `custom-model-selected-for-local-whisper` in `src/AskLucy.Web/Controllers/v1/AdminCustomModelsController.cs`. Make T025 pass. _(Done: the 409 mapping is in `ProblemDetailsMiddleware`, alongside the other domain exceptions.)_
- [X] T033 [US4] Create `src/AskLucy.Application/Ai/Dictation/Queries/GetDictationSettings/` (query, handler, DTOs matching contracts/admin-dictation.md):
  - Fields: `primaryEngine`, `pushToTalkEngine`, `state`, `suspension`, `lastRevert`, `engines`, `pushToTalkEngines`, `localWhisper { selectedModelId, effectiveModel, models }` and `rowVersion`.
  - Engine selectability comes from the AI provider rows' `IsEnabled`, with the same fallback `ElevenLabsSpeechToTextSessionProvider.IsSwitchedOnAsync` uses.
- [X] T034 [US4] Create `src/AskLucy.Application/Ai/Dictation/Commands/SelectLocalWhisperModel/` (command, validator, handler; checks `ILocalWhisperModelCatalog.CheckSelectableAsync`, then `setting.SelectLocalWhisperModel`, then commits).
- [X] T035 [US4] Create `src/AskLucy.Application/Ai/Dictation/Commands/TryLocalWhisperModel/` (command, validator, handler) and a transient try entry point on `LocalWhisperTranscriber` in `src/AskLucy.Infrastructure/Ai/LocalWhisper/LocalWhisperTranscriber.cs`:
  - The factory is not cached, is disposed after the call, and one try runs at a time.
  - The result is the transcript, elapsed ms and model label.
  - Failures are logged with `[LoggerMessage]`, not put on the trail. Make T026 pass.
- [X] T036 [US4] Add the routes `GET dictation`, `PUT dictation/local-whisper-model` and `POST dictation/try` (`[RequestSizeLimit(4 MB)]`, multipart) to `src/AskLucy.Web/Controllers/v1/AdminVoiceProvidersController.cs`. Use the existing Administrator policy and `admin-endpoints` rate limit; map the Problem Details types from the contract. Make T027 pass.
- [X] T037 [P] [US4] Add `getDictationSettings`, `selectLocalWhisperModel` and `tryLocalWhisperModel` to `src/AskLucy.Web/ClientApp/src/features/admin/api/adminVoiceApi.ts`. Rename `ignoredFilePath` → `filePath` and add `sourceFilePath` and `selectedForLocalWhisper` in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminCustomModelsApi.ts`.
- [X] T038 [US4] Create `src/AskLucy.Web/ClientApp/src/features/admin/components/DictationSettingsSection.tsx` (the model part) and render it on `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminVoicePage.tsx`:
  - The Local Whisper model picker and the `effectiveModel.problem` text. With no model, the text points the admin to deploy `ggml-base.bin` under Custom Models.
  - Try it: record with `wavEncoder.ts`, then show the transcript and elapsed ms.
  - Every mutation has an error toast.
- [X] T039 [P] [US4] Update the customModels components in `src/AskLucy.Web/ClientApp/src/features/admin/components/customModels/`:
  - `AddCustomModelDialog.tsx`: the notice becomes "Only <filePath> will be deployed".
  - Add a "Local Whisper" chip on the selected deployment.
  - `RemoveCustomModelButton.tsx`: disabled with the "Select a different Local Whisper model first" tooltip. Make T028 pass.

**Checkpoint**: US4 is testable alone. A single-file deploy transfers one file, and a deployed ggml model can be tried, selected and protected from removal.

---

## Phase 4: User Story 1 - Dictation runs on Local Whisper by default (Priority: P1) 🎯 MVP

**Goal**: With a Local Whisper model selected, Push-to-Talk and Continuous dictation are transcribed by Local Whisper, with no vendor call and no notice. On a fresh deployment with no model, both use the browser built-in as the normal path.

**Independent Test**: See quickstart scenario 1.
- Fresh database: dictation works through the browser built-in, with no degraded notice and no trail entry.
- After deploying and selecting `ggml-base.bin` (US4): both modes are transcribed by Local Whisper, with no OpenAI or ElevenLabs request.
- Arabic dictation returns Arabic script (quickstart scenario 2).

### Tests for User Story 1

- [X] T040 [P] [US1] Rewrite `tests/AskLucy.Application.Tests/Ai/CreateSpeechToTextSessionCommandHandlerTests.cs` for the Local Whisper rows of the research D6 table, for both `mode` values:
  - No model selected → `Browser`, `degraded: false`, nothing reported.
  - Model Unavailable → `Browser`, `degraded: false`.
  - Model Ready → `Clip`.
  - `mode` absent defaults to Continuous.
  - No network call is made for Local Whisper.
- [X] T041 [P] [US1] Application tests in `tests/AskLucy.Application.Tests/Ai/Dictation/TranscribeDictationClipCommandHandlerTests.cs`:
  - Resolves the clip engine through the setting and calls exactly that transcriber.
  - Success → `ReportServed` and the transcript.
  - Not configured (no model / Unavailable) → the unavailable result with nothing reported.
- [X] T042 [P] [US1] Web tests in `tests/AskLucy.Web.Tests/Ai/AiControllerVoiceTests.cs`:
  - `POST /api/v1/ai/voice/transcriptions` requires auth, enforces 4 MB, rejects non-WAV with 422 `dictation-audio-invalid` (header read, content type ignored), and returns `{ text, language }`.
  - `stt-session` accepts `mode` and returns `degraded`.
  - The legacy `POST /api/v1/ai/transcriptions` is unchanged.
  - Load the env first: `eval "$(python $S/webenv.py src/AskLucy.Web/appsettings.Development.json)"`.
- [X] T043 [P] [US1] Frontend tests:
  - `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceRecorder.test.ts`: Push-to-Talk calls stt-session before opening the mic, `Clip` → posts WAV to `/ai/voice/transcriptions`, and `Browser` with `degraded: false` → uses the browser built-in with no notice.
  - `useSpeechRecognition.test.ts`: the same for Continuous.
  - Extend `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.test.ts` for the new call.

### Implementation for User Story 1

- [X] T044 [US1] Update `src/AskLucy.Application/Ai/Commands/CreateSpeechToTextSession/DictationSession.cs`:
  - `DictationEngine { Realtime, Clip, Browser }`: rename `Whisper` → `Clip`.
  - Add `bool Degraded`.
  - Add optional `Mode` (default Continuous) to `CreateSpeechToTextSessionCommand.cs`.
- [X] T045 [US1] Rewrite `src/AskLucy.Application/Ai/Commands/CreateSpeechToTextSession/CreateSpeechToTextSessionCommandHandler.cs` around `setting.ResolveEngine(mode)` and the research D6 table. This story implements the Local Whisper and Browser rows; the OpenAI Whisper and ElevenLabs realtime rows keep today's behavior until US3. Make T040 pass.
- [X] T046 [US1] Create `src/AskLucy.Application/Ai/Dictation/Commands/TranscribeDictationClip/` (command, validator for language regex and non-empty file, handler per contracts/dictation-transcription.md steps 1–4). Report success with `IVoiceFailureReporter.ReportServed` using `VoiceEngineIdentity("Local Whisper", null, <model file>)`. Make T041 pass.
- [X] T047 [US1] Add the `POST voice/transcriptions` action to `src/AskLucy.Web/Controllers/v1/AiController.cs`:
  - `[Authorize]`, `[EnableRateLimiting("ai-endpoints")]`, `[RequestSizeLimit(4 MB)]`.
  - The WAV header is validated server-side via `WavHeader`: invalid → 422 `dictation-audio-invalid`; not served → 503 `dictation-engine-unavailable`.
  - Pass `mode` through on `voice/stt-session`. Make T042 pass.
- [X] T048 [P] [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/api/voiceApi.ts` and `aiApi.ts`: the `'Realtime' | 'Clip' | 'Browser'` engine union, `mode`, `degraded`, and `transcribeDictationClip(wav, language)`.
- [X] T049 [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceRecorder.ts` (Push-to-Talk):
  - Call stt-session with `mode: 'PushToTalk'` before opening the mic.
  - On `Clip`, record, convert with `wavEncoder.ts` and post to the new endpoint.
  - On `Browser`, dictate through the browser built-in, showing the notice only when `degraded`.
  - Every promise is awaited or caught with a visible error path (CLAUDE.md Error Handling).
- [X] T050 [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.ts` and `dictationFallback.ts` (Continuous):
  - The `Clip` path posts WAV to `/ai/voice/transcriptions` instead of `/ai/transcriptions`.
  - `Browser` + `degraded: false` shows no notice.
  - Only one listener holds the mic at a time (FR-005a). Make T043 pass.

**Checkpoint**: MVP complete (Phases 1–4). Dictation works on a fresh deployment through the browser built-in, and on Local Whisper once a model is deployed and selected. This is safe to deploy.

---

## Phase 5: User Story 2 - Local Whisper fails over to the browser, never to a paid engine (Priority: P1)

**Goal**: Any Local Whisper failure is recorded on the trail, and the user gets the gentle repeat. The next attempt uses the browser built-in; no other engine is ever tried.

**Independent Test**: See quickstart scenarios 3 and 4.
- Rename the selected model file. The next dictation goes to the browser built-in with a *Local Whisper* failover on the trail. Restore the file, and Local Whisper serves again.
- Block `/api/v1/ai/voice/transcriptions` in DevTools. Lucy shows and (with voice replies on) speaks "Sorry, I missed that — could you say it again?", then listens through the browser built-in.

### Tests for User Story 2

- [ ] T051 [P] [US2] Extend `tests/AskLucy.Application.Tests/Ai/CreateSpeechToTextSessionCommandHandlerTests.cs`: a selected model that is `Broken` → `Browser`, `degraded: true`, and `ReportFailover(Transcription, "Local Whisper", fallbackServed: true)`.
- [ ] T052 [P] [US2] Extend `tests/AskLucy.Application.Tests/Ai/Dictation/TranscribeDictationClipCommandHandlerTests.cs`:
  - A transcriber exception → `ReportFailover(..., fallbackServed: true)` and the unavailable result, with no second transcriber called.
  - An invalid WAV → `ReportFailure` (`ValidationFailed`).
  - Local Whisper failures never suspend.
  - The reason passes through `FailureReasonSanitizer`.
- [ ] T053 [P] [US2] Frontend tests:
  - `src/AskLucy.Web/ClientApp/src/features/chat/voice/gentleRepeat.test.ts`: all 5 languages, falling back to English.
  - `useVoiceRecorder.test.ts` and `useSpeechRecognition.test.ts`: any non-200 or network error shows the gentle repeat; the next attempt only is marked "browser built-in"; Continuous restarts listening after the message; the attempt after that calls stt-session again.
  - `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx`: the message is spoken through `useVoiceOutput` when voice replies are on.
  - If `ChatPage.test.tsx` flakes under the full suite, re-run it in isolation (memory).

### Implementation for User Story 2

- [ ] T054 [US2] Create `src/AskLucy.Application/Ai/Dictation/DictationFailurePolicy.cs`. It is the single place that classifies a failure (`IFailureClassifier`), reports it through `IVoiceFailureReporter` with the glossary engine name, and returns whether to suspend (always false for Local Whisper; the cloud engines are wired up in US3). Use it from the stt-session and clip handlers. Make T051 and T052 pass.
- [ ] T055 [US2] Add the Broken branch to `src/AskLucy.Application/Ai/Commands/CreateSpeechToTextSession/CreateSpeechToTextSessionCommandHandler.cs` and the exception path to `src/AskLucy.Application/Ai/Dictation/Commands/TranscribeDictationClip/TranscribeDictationClipCommandHandler.cs`, both through `DictationFailurePolicy`. Keep the user-facing Problem Details generic (FR-007).
- [ ] T056 [P] [US2] Create `src/AskLucy.Web/ClientApp/src/features/chat/voice/gentleRepeat.ts`: an en/ar/es/fr/de phrase map keyed by dictation language, with an English fallback.
- [ ] T057 [US2] Wire the gentle repeat:
  - In `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceRecorder.ts`, `useSpeechRecognition.ts` and `dictationFallback.ts`: show the message and switch the next attempt to the browser built-in.
  - In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`: speak it through `useVoiceOutput` (persona voice) when voice replies are on.
  - In `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.ts`: remove or align the `FALLBACK_NOTICES` inline copy with the new flow. Make T053 pass.

**Checkpoint**: US1 and US2 both work. Every Local Whisper failure is on the trail and recovered through the browser built-in.

---

## Phase 6: User Story 3 - An administrator changes the primary dictation engine (Priority: P1)

**Goal**: An admin sets the primary engine (Local Whisper / OpenAI Whisper / ElevenLabs realtime; cloud options only while switched on) and the Push-to-Talk engine used under ElevenLabs realtime. Behavior when a vendor lapses or is switched off:
- A subscription-type failure suspends only that engine's paths to the browser built-in, with an admin banner and nav badge.
- Switching a vendor off reverts any choice that used it to Local Whisper.

**Independent Test**: See quickstart scenarios 5, 5a and 6.
- Primary = OpenAI Whisper dictates via OpenAI.
- Switching OpenAI off reverts the primary to Local Whisper, and the reason is shown.
- Primary = ElevenLabs realtime: Continuous streams, and Push-to-Talk goes to Local Whisper.
- An invalid OpenAI credential suspends to the browser built-in, with a Critical incident, banner and badge. Setting the primary again clears it.

### Tests for User Story 3

- [ ] T058 [P] [US3] Extend `tests/AskLucy.Application.Tests/Ai/CreateSpeechToTextSessionCommandHandlerTests.cs` for the remaining research D6 rows:
  - OpenAI Whisper healthy → `Clip`; switched off or credential unresolvable → `Browser`, `degraded: true`, with a failover.
  - ElevenLabs realtime Continuous → `Realtime` + token.
  - ElevenLabs realtime Push-to-Talk → the Push-to-Talk engine (`Clip` for LocalWhisper/OpenAiWhisper; `Browser`, not degraded, for Browser). No token is minted.
  - Suspended for that vendor → `Browser`, `degraded: true`, no vendor call.
  - A Critical mint failure suspends and returns `200 Browser`; a transient one returns 503.
- [ ] T059 [P] [US3] Extend `tests/AskLucy.Application.Tests/Ai/Dictation/TranscribeDictationClipCommandHandlerTests.cs`:
  - A Critical OpenAI Whisper failure suspends and commits.
  - A transient one doesn't suspend.
  - A stale failure from an engine no longer selected doesn't suspend.
  - Under an ElevenLabs suspension, a Push-to-Talk clip on Local Whisper is still served.
- [ ] T060 [P] [US3] Application tests in `tests/AskLucy.Application.Tests/Ai/Dictation/DictationEngineAdminCommandTests.cs`:
  - `SetDictationPrimaryEngineCommand` refuses a switched-off vendor (FR-004) and clears a suspension.
  - `SetPushToTalkEngineCommand` refuses OpenAI Whisper while OpenAI is off.
  - `DictationEngineSettingSwitchOffObserver` reverts the primary and/or Push-to-Talk engine.
  - Create `tests/AskLucy.Application.Tests/Ai/UpdateAiProviderCommandHandlerTests.cs` (none exists today) to check that observers are called only on the enabled→disabled edge and before the single `SaveChangesAsync`.
- [ ] T061 [P] [US3] Infrastructure test in `tests/AskLucy.Infrastructure.Tests/Ai/OpenAiWhisperClipTranscriberTests.cs`: it delegates to the OpenAI transcription call with `whisper-1` and the WAV stream, and surfaces provider exceptions unchanged for classification.
- [ ] T062 [P] [US3] Extend the Web tests in `tests/AskLucy.Web.Tests/Ai/AdminDictationControllerTests.cs` for `PUT …/dictation/primary` and `PUT …/dictation/push-to-talk`: 204, 422 `dictation-engine-not-selectable`, 409 stale `rowVersion`, Administrator only. Extend `tests/AskLucy.Web.Tests/Ai/AdminVoiceProvidersControllerTests.cs` if the route table assertions live there.
- [ ] T063 [P] [US3] Frontend tests:
  - `src/AskLucy.Web/ClientApp/src/features/admin/components/DictationSettingsSection.test.tsx`: the primary picker with disabled vendor options and their reasons; the Push-to-Talk engine shown only under ElevenLabs realtime; the Suspended banner; the revert notice.
  - `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminVoicePage.a11y.test.tsx`: axe on the new section.
  - The admin nav badge test next to `src/AskLucy.Web/ClientApp/src/features/admin/adminNav.tsx`.

### Implementation for User Story 3

- [ ] T064 [P] [US3] Create `src/AskLucy.Infrastructure/Ai/OpenAiWhisperClipTranscriber.cs` (`IDictationClipTranscriber`, Engine = `OpenAiWhisper`, an adapter over `IAIProvider.TranscribeAudioAsync` of the OpenAI provider, preferring the DB credential per the existing credential resolution). Register it in `src/AskLucy.Infrastructure/DependencyInjection.cs`. Make T061 pass.
- [ ] T065 [US3] Complete the research D6 table in `src/AskLucy.Application/Ai/Commands/CreateSpeechToTextSession/CreateSpeechToTextSessionCommandHandler.cs`:
  - OpenAI Whisper local health check.
  - ElevenLabs realtime mint for Continuous only.
  - The Push-to-Talk engine under ElevenLabs realtime.
  - `IsSuspendedFor` short-circuits.
  - Make T058 pass.
- [ ] T066 [US3] Extend `src/AskLucy.Application/Ai/Dictation/DictationFailurePolicy.cs`: a Critical kind (`OperationalFailureSeverityPolicy.IsCritical`) from a selected cloud engine → `setting.Suspend(engine, sanitizedReason, now)`, committed by the calling handler; log the first suspension with `[LoggerMessage]`. Make T059 pass.
- [ ] T067 [US3] Create `src/AskLucy.Application/Ai/Dictation/Commands/SetDictationPrimaryEngine/` and `src/AskLucy.Application/Ai/Dictation/Commands/SetPushToTalkEngine/` (command, validator, handler; vendor switched-on check, then the domain method, then commit with `rowVersion`).
- [ ] T068 [US3] Add `IAiProviderSwitchedOffObserver` notification on the enabled→disabled edge, before `SaveChangesAsync`, in `src/AskLucy.Application/Ai/Commands/UpdateAiProvider/UpdateAiProviderCommandHandler.cs`. Create `src/AskLucy.Application/Ai/Dictation/DictationEngineSettingSwitchOffObserver.cs`, and register it in the Application DI (`src/AskLucy.Application/DependencyInjection.cs`). Make T060 pass.
- [ ] T069 [US3] Add `PUT dictation/primary` and `PUT dictation/push-to-talk` to `src/AskLucy.Web/Controllers/v1/AdminVoiceProvidersController.cs`. Make T062 pass.
- [ ] T070 [P] [US3] Add `setDictationPrimaryEngine` and `setPushToTalkEngine` to `src/AskLucy.Web/ClientApp/src/features/admin/api/adminVoiceApi.ts`.
- [ ] T071 [US3] Extend `src/AskLucy.Web/ClientApp/src/features/admin/components/DictationSettingsSection.tsx`:
  - The primary engine picker (disabled options show `unavailableReason`).
  - The Push-to-Talk engine picker, shown only while the primary is ElevenLabs realtime.
  - The "Suspended — browser built-in in use" banner (engine, reason, time) and the `lastRevert` notice.
  - A 409 prompts a reload.
- [ ] T072 [US3] Add a Suspended badge on the Voice entry in `src/AskLucy.Web/ClientApp/src/features/admin/adminNav.tsx` (line ~85), driven by the dictation settings query. Make T063 pass.

**Checkpoint**: All four stories work independently. The whole engine policy is admin-controlled.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Docs, cleanup, full verification.

- [ ] T073 [P] Correct the "Superseded" note in `specs/012-elevenlabs-voice-engine/research.md` (~line 86): Local Whisper returns as the default. Point `specs/012-elevenlabs-voice-engine/contracts/voice-stt-session.md` at `specs/078-restore-local-whisper/contracts/dictation-session.md`.
- [ ] T074 [P] Correct the same superseded note in `docs/adr/0006-elevenlabs-voice-engine-integration.md` (~line 90).
- [ ] T075 [P] Update the "Switched off is not a failure" note in `specs/070-voice-provider-admin/spec.md` (~line 105): it still holds for voice generation, and dictation now reverts (FR-015) or suspends (FR-016).
- [ ] T076 [P] Update `specs/072-custom-model-deploy/contracts/admin-custom-models.md`, `specs/072-custom-model-deploy/contracts/custom-model-deployment-hub.md` and `docs/adr/0016-custom-model-deployment-temporary-ftp-target.md` for single-file deploys (`filePath`, `sourceFilePath`, the selected-model removal guard).
- [ ] T077 [P] Write `docs/adr/0018-dictation-engine-policy.md`:
  - Local Whisper as default; nothing auto-downloaded, so a fresh deployment uses the browser built-in.
  - The browser built-in as the only fallback.
  - The Push-to-Talk engine under ElevenLabs realtime.
  - Suspend vs revert.
  - The observer instead of a domain event.
  - Browser-side WAV conversion.
  - Update the ADR index if one exists in `docs/adr/`.
- [ ] T078 Search `src/` and `tests/` for leftover references to the old dictation route and engine value (`DictationEngine.Whisper`, `'Whisper'` engine string, dictation calls to `/ai/transcriptions`) and fix them. Use the Grep tool, not a recursive shell grep over `src` (it times out).
- [ ] T079 Backend verification:
  - `dotnet build "Ask Lucy.sln" -v q -nologo -m:1` (background).
  - `dotnet test` for Domain, Application, Infrastructure and Web (with the webenv).
  - Persistence against test2 only.
  - `dotnet format "Ask Lucy.sln" --no-restore --verify-no-changes --severity error --include <changed files>`.
- [ ] T080 Frontend verification in `src/AskLucy.Web/ClientApp`: `npx tsc -b --noEmit` (never bare `tsc --noEmit`), then the FULL `npx vitest run`. Re-run any ChatPage flake in isolation before judging it. Also run `npm ci` if `package-lock.json` changed (memory: Windows install prunes @emnapi).
- [ ] T081 Run the quickstart manual scenarios on localhost:7170 (`specs/078-restore-local-whisper/quickstart.md` 1–9), then on production after deploy:
  - Deploy and select `ggml-base.bin` there.
  - Confirm `App_Data/whisper-models/ggml-BaseEn.bin` is still present (scenario 10). It must never be deleted by code.
- [ ] T082 Commit in logical groups and push to main:
  - Groups: foundation, US4, US1, US2, US3, docs.
  - Verify each commit with `git show --stat`, since a concurrent session may reset the tree.
  - Never stage `graphify-out/`, `.codex/`, `.specify/feature.json`, `specs/067-*`, `specs/069-*`, `specs/071-*`, `specs/079-*` or the Smtp change in `src/AskLucy.Web/appsettings.json`.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: depends on Setup and blocks every story.
- **US4 (Phase 3)**: depends on Foundational.
- **US1 (Phase 4)**: depends on Foundational. Its end-to-end check needs a model selected through US4.
- **US2 (Phase 5)**: depends on US1 (it adds failure branches to the US1 handlers and hooks).
- **US3 (Phase 6)**: depends on US1 (the D6 table) and on US2 (`DictationFailurePolicy`). T071 extends the component US4 creates in T038.
- **Polish (Phase 7)**: depends on all stories.

### Within each story

Tests are written first and fail. Then Domain/Application, then Infrastructure/Web, then frontend. Tasks touching the same file run sequentially:
- `CreateSpeechToTextSessionCommandHandler.cs`: T045 → T055 → T065.
- `DictationSettingsSection.tsx`: T038 → T071.
- `useVoiceRecorder.ts`: T049 → T057.

### Parallel opportunities

- Phase 2: T004–T009 (tests) together, then T010, T012, T013, T014, T017, T018 and T022 together.
- US4: T023–T028 together; T037 and T039 alongside the backend tasks.
- US1: T040–T043 together; T048 alongside T044–T047.
- US3: T058–T063 together; T064 and T070 alongside the handler work.
- Polish: T073–T077 together.

## Parallel Example: User Story 4

```text
Task: "T023 HuggingFaceModelSourceTests FilePath rename"
Task: "T024 CustomModelDeploymentJobTests single-file filter"
Task: "T025 RemoveCustomModelCommandHandlerTests FR-009b guard"
Task: "T026 LocalWhisperModelAdminTests"
Task: "T027 AdminDictationControllerTests + AdminCustomModelsControllerTests"
Task: "T028 AddCustomModelDialog + DictationSettingsSection tests"
```

## Implementation Strategy

### MVP first (Phases 1–4)

1. Setup, then Foundational.
2. US4: a single-file deploy, and selecting a Local Whisper model.
3. US1: dictation served by Local Whisper, and by the browser built-in when no model is selected.
4. **Stop and validate** quickstart scenarios 1, 2, 7 and 8, then deploy.

**Heads-up — behavior change**: production clips go to OpenAI Whisper today. After this deploy,
dictation uses the browser built-in until an admin deploys and selects `ggml-base.bin` under
Custom Models.

### Incremental delivery

1. MVP (US4 + US1), then deploy and select `ggml-base.bin` on production.
2. Add US2 (failure trail and gentle repeat), then deploy.
3. Add US3 (admin engine choice, Push-to-Talk engine, suspend and revert), then deploy.
4. Polish: docs and the ADR.

## Notes

- [P] tasks touch different files and don't depend on incomplete tasks.
- Commit after each logical group; pushing to main deploys to production.
- `ggml-BaseEn.bin` on production must never be deleted by code (FR-014).
- Out of scope, flagged: removing Completed Custom Models (a specs/072 extension), and email notification of a suspension (specs/067).
