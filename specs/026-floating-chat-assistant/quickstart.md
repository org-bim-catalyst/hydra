# Quickstart: Validating the Floating Chat Assistant Redesign

## Prerequisites

- Backend (`AskLucy.Web`) running locally with the `defaultLanguage` migration applied and a signed-in test account.
- Frontend dev server:

```sh
cd src/AskLucy.Web/ClientApp
npm install
npm run dev
```

- Sign in, then navigate to `/studio`.
- A browser/OS with microphone permission available (Push-to-Talk/recording scenarios need real `getUserMedia` — use a real browser, not a headless environment, for scenarios 5–6).

## Scenario 1 — Collapsed by default, unobstructed (US1)

1. Load `/studio` fresh (hard refresh).
2. **Expect**: the chat widget renders only as a narrow vertical strip — expand handle, voice analyzer, Push-to-Talk, Continuous Listening toggle, Mute Agent, status indicator — nothing else (FR-002/FR-003). The design viewer behind it is fully visible; the strip never overlaps it (FR-005).
3. Send a message via another already-open tab/session on the same account (or trigger a reply some other way) while this tab sits idle.
4. **Expect**: the analyzer visibly shifts between Idle/Processing/Speaking as the assistant generates and speaks a reply, even while collapsed (FR-004).

## Scenario 2 — Expanding into the full conversation (US2)

1. Activate the handle.
2. **Expect**: a smooth (~300ms, reduced-motion-aware) transition into the Expanded state; the vertical analyzer disappears (FR-007); the header shows Lucy's identity + online status + a language flag; message history, composer, and voice controls are all present (FR-008).
3. Collapse it again via the same handle.
4. **Expect**: it returns to Collapsed smoothly, and the underlying Studio viewer/other controls were never blocked or altered throughout (FR-011).

## Scenario 3 — New conversation by default, minimal manual option (US3)

1. Reload `/studio`.
2. **Expect**: no "+ New chat" button anywhere in either state (FR-012); expanding shows an already-active, empty conversation with no action taken (FR-013).
3. Send a message, then activate the small icon-only new-chat control in the Expanded header.
4. **Expect**: a new empty conversation becomes active immediately, without a page reload (FR-014).
5. Open Settings > Chat History.
6. **Expect**: the conversation from step 3 is listed and reopenable exactly as before this feature (FR-013).

## Scenario 4 — Active language as a flag (US4)

1. In the Expanded header, confirm no language dropdown is present (FR-015) — only a small circular flag.
2. Open Settings > Chat Configuration, change the default language.
3. **Expect**: the save succeeds (or a visible error if it fails — never silent) and, back on `/studio`, the flag in the chat widget's header reflects the new language (FR-016/FR-017).

## Scenario 5 — Push-to-Talk recording review (US5)

1. From either widget state, activate Push-to-Talk and speak.
2. **Expect**: a live waveform of your speech (no live partial transcript text anywhere) — FR-019.
3. Activate "finished speaking."
4. **Expect**: capture stops; cancel and accept/send controls appear; nothing has been sent to any server yet (FR-020) — verify via the browser's network panel that no transcription request fired before this point.
5. Activate cancel.
6. **Expect**: no transcript appears in the composer, no network request was made, and the UI returns to normal typing (FR-021).
7. Repeat steps 2–4, then activate accept/send instead.
8. **Expect**: exactly one request to `/api/v1/ai/transcriptions` fires now (not earlier), and its result is used exactly as existing voice-to-text input is used today (FR-022).
9. Repeat step 2, then collapse the widget mid-recording.
10. **Expect**: the recording is discarded, not left running invisibly (FR-024); the widget returns to its idle Collapsed state.
11. Switch to Continuous Listening and speak a full turn.
12. **Expect**: behavior is unchanged from before this feature — no waveform-review/finish/cancel/send step appears at any point (FR-025).

## Scenario 6 — No standalone image-generation button (US6)

1. In the Expanded state, inspect the composer's available actions.
2. **Expect**: no "Generate image" button anywhere (FR-018).

## Regression check — existing behavior preserved (FR-026/FR-027)

1. Send a text message; confirm it streams a reply exactly as before.
2. Attach a file, use "Insert saved prompt," and use the Translate action on a reply.
3. **Expect**: all continue to work unchanged.

## Automated checks

```sh
cd src/AskLucy.Web/ClientApp
npm run test      # vitest — component behavior + useVoiceRecorder unit tests
npm run lint       # ESLint/Prettier
npx tsc --noEmit -p .
```

```sh
cd src/AskLucy.Web
dotnet test        # backend unit + integration tests, including the extended UserVoicePreference slice
```

- `ChatAssistantWidget`, `CollapsedChatControl`, and `ExpandedChatPanel` should each have a matching `*.a11y.test.tsx` (jest-axe, zero violations), since this widget does not inherit `CircularAction`'s existing a11y coverage (research.md #9).
- `useVoiceRecorder.test.ts` should cover: `accept()` is the only path that calls `transcribeAudio` (assert the mock is uncalled through `recording`/`reviewing`, called exactly once after `accept()`); `cancel()` from either `recording` or `reviewing` never calls it; discarding on collapse (FR-024).
- Backend: a validator test asserting `defaultLanguage` outside the supported set is rejected with `400`, and an integration test round-tripping `PUT`/`GET /api/v1/ai/voice/preferences` with `defaultLanguage` set.

## Definition of done for this quickstart

All six scenarios plus the regression check pass manually in a real browser with real microphone access, automated frontend and backend tests pass, and `git grep -r "LanguageSelector\|AssistantPanel" src/AskLucy.Web/ClientApp/src` returns no remaining references (both are fully removed, not left dead).
