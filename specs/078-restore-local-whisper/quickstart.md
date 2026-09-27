# Quickstart: Validating Local Whisper Dictation

**Feature**: specs/078-restore-local-whisper. Contracts: [contracts/](contracts/). Data model:
[data-model.md](data-model.md).

Engine names: **Local Whisper**, **OpenAI Whisper**, **ElevenLabs realtime**, **Browser built-in**.

## Prerequisites

- Backend builds: `dotnet build "Ask Lucy.sln" -v q -nologo -m:1`.
- Migration `RestoreLocalWhisperDictation` applied to the database you run against. For
  Persistence tests, apply it by hand to test2 only (`db_a15752_asklucytest2`), never to the shared
  test DB.
- Frontend: `cd src/AskLucy.Web/ClientApp && npx tsc -b --noEmit && npx vitest run` (the full
  suite).
- Web.Tests: load the env from `appsettings.Development.json` (webenv pattern) first.

## Automated checks

| Check | Command | Proves |
|---|---|---|
| Domain | `dotnet test tests/AskLucy.Domain.Tests --filter DictationEngineSetting` | Transitions: SetPrimary clears suspension; Suspend refused for Local Whisper and for an unselected engine; ResolveEngine picks the Push-to-Talk engine only under ElevenLabs realtime; revert covers primary and Push-to-Talk engine. |
| Application | `dotnet test tests/AskLucy.Application.Tests --filter "Dictation\|CreateSpeechToTextSession\|RemoveCustomModel"` | stt-session decision table (research D6) incl. the not-configured `Browser` (not degraded) rows; a Critical kind suspends, a transient one doesn't; no second transcriber is ever called; FR-009b guard. |
| Infrastructure | `dotnet test tests/AskLucy.Infrastructure.Tests --filter "LocalWhisper\|WavHeader\|CustomModelDeployment"` | WAV header validation; the ggml magic check; single-file listing filter; a missing named file fails the deployment. |
| Web | `dotnet test tests/AskLucy.Web.Tests --filter "Dictation"` | Routes, auth, the 4 MB limit, Problem Details types, admin 409/422. |
| Frontend | `npx vitest run src/features/chat/voice src/features/admin` | WAV encoder output header; stt-session → Browser path; gentle repeat shown (and spoken when voice replies are on); Push-to-Talk calls stt-session first; admin Dictation section. |

## Manual scenarios (localhost:7170, then production)

1. **Fresh deployment → browser built-in, then Local Whisper (US1, US4, SC-001)**. On a fresh
   database, open Admin → Voice → Dictation. It shows *Primary: Local Whisper* and "No Local
   Whisper model is selected, so dictation uses the browser built-in". Dictate "Hello Lucy" in
   Push-to-Talk and Continuous: both work through the browser built-in with no degraded notice,
   and Admin → Operational failures shows no new entry. Now Admin → AI providers → Custom Models →
   Add `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin`, destination
   `App_Data/whisper-models/base`; the preview says "Only ggml-base.bin will be deployed". When it
   completes, select it in Dictation. Dictate again: both transcripts come from Local Whisper
   (server log: one "Local Whisper model loaded" line on the first clip, none at startup).
2. **Arabic**. Switch the chat language to Arabic and dictate a short Arabic sentence. The
   transcript is Arabic script (the multilingual model; `ggml-BaseEn.bin` would give English
   phonetics).
3. **Failure → browser built-in only (US2)**. Temporarily rename the selected
   `App_Data/whisper-models/base/ggml-base.bin` (localhost only). The next dictation goes straight to the browser built-in, and the trail shows a
   *Local Whisper* transcription failover. Restore the file; the next attempt uses Local Whisper
   again.
4. **Gentle repeat (FR-005b)**. With DevTools, block `/api/v1/ai/voice/transcriptions`. Speak in
   Continuous mode. Lucy shows "Sorry, I missed that — could you say it again?", speaks it in her
   voice when voice replies are on, then listens again through the browser built-in.
5. **Admin switch (US3)**. Set Primary = OpenAI Whisper (OpenAI switched on) and dictate; the trail
   shows no failure. Switch OpenAI off under Admin → AI providers. Admin → Voice → Dictation now
   shows *Primary: Local Whisper — reverted from OpenAI Whisper because OpenAI was switched off*
   (FR-015).
5a. **Push-to-Talk under ElevenLabs realtime (US3, FR-017)**. Set Primary = ElevenLabs realtime.
   The Push-to-Talk engine shows *Local Whisper*. Dictate in Continuous: it streams through
   ElevenLabs. Dictate in Push-to-Talk: the server log shows Local Whisper transcribed the clip and
   no ElevenLabs request was made. Set the Push-to-Talk engine to *Browser built-in*: the next
   press uses the browser built-in with no degraded notice.
6. **Subscription lapse (FR-016, SC-006)** — localhost only. Set Primary = OpenAI Whisper and give
   OpenAI an invalid credential. Dictate once. The trail shows a **Critical** incident, Admin →
   Voice shows the *Suspended — browser built-in in use* banner, and the admin nav shows a badge.
   Dictate again: the browser built-in is used, and no request reaches OpenAI (server log / no new
   OpenAI incident occurrence). Set Primary again → the banner clears.
7. **Single-file deploy (US4, SC-005)**. Admin → AI providers → Custom Models → Add:
   `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin`, destination
   `App_Data/whisper-models/small`. The preview says "Only ggml-small.bin will be deployed", and the
   progress total ≈ 488 MB, not the repository's tens of GB. When it completes, Dictation still
   uses the selected `ggml-base.bin` (FR-009a).
8. **Try it, then select (FR-009c, FR-009a)**. In Dictation, pick *whisper.cpp (ggml-small.bin)*
   → Try it → record 5 s. The transcript and elapsed ms appear, and the selection is unchanged.
   Select it. The next user dictation uses it (server log names the model file). Try to remove the
   deployment under Custom Models: refused with "Select a different Local Whisper model first".
9. **Unavailable model → browser built-in (FR-010)**. Mark the selected deployment Unavailable.
   Dictation uses the browser built-in with no degraded notice, nothing goes on the trail, and the
   Dictation section explains why.
10. **English-only file untouched (FR-014)**. On production, `App_Data/whisper-models/ggml-BaseEn.bin`
    is still present after deploy and is never read. Retire it by hand only after scenarios 1–2
    pass on production.

## Performance note (measured, not gated)

Record the elapsed ms from Try it for a 10 s clip on production (shared site4now CPU) with
`ggml-base.bin`. Local Whisper's `MaxConcurrentTranscriptions`/`QueueTimeoutSeconds` defaults
(2 / 10 s) are revisited if p95 for a 10 s clip exceeds 5 s.
