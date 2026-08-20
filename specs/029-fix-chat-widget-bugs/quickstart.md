# Quickstart: Validating the Chat Widget Fixes

Prerequisites: local dev stack running (backend `dotnet run` from `src/AskLucy.Web`,
frontend `npm run dev` from `src/AskLucy.Web/ClientApp`), authenticated as any user, a
microphone available to the browser for the voice checks.

## 1. Voice preferences no longer 500 (Bug 1 / FR-001–FR-003, FR-012)

1. Apply pending migrations to your local database (`dotnet ef database update` from
   `src/AskLucy.Persistence`, or via the backend's existing migration-apply step).
2. `curl -i https://localhost:<port>/api/v1/ai/voice/preferences` with a valid bearer
   token → expect `200 OK`, not `500`.
3. `curl -i https://localhost:<port>/health/ready` → expect `200 OK` with
   `"status": "Healthy"` (see [contracts/health-readiness-endpoint.md](contracts/health-readiness-endpoint.md)).
4. To verify the readiness check actually catches drift: temporarily roll back one
   migration (`dotnet ef database update <previous-migration>`), re-curl `/health/ready`
   → expect `503` naming the pending migration. Roll forward again afterward.
5. Open the chat window in the browser → confirm no full-width red "An unexpected error
   occurred" Snackbar appears on load (SC-001).

## 2. Voice-preferences fetch failure degrades quietly, not silently (Decision 3)

1. With the app running, temporarily point the frontend's API base URL at a host that
   will fail the voice-preferences request (or stop the backend after the SPA has
   loaded, then reload just the chat panel if your dev setup allows it).
2. Confirm: no blocking full-width Snackbar; instead, a small, dismissible indicator is
   visible near the mic control indicating default voice settings are in use.
3. Confirm chat sending, receiving, and the mic control itself still work normally under
   this failure (FR-002).
4. Check the backend's Serilog output/sink → confirm the failure is present at Error
   level (FR-003) — i.e. it did not become invisible to operators just because the UI is
   calmer.

## 3. Exactly one mic control, in both modes (Bug 2 / FR-004–FR-006a, SC-002)

1. Open the chat window, expand it, open the mic's mode menu, switch to **Push-to-Talk**.
2. Confirm exactly one mic icon is visible anywhere in the composer/voice-control row —
   not two.
3. Press and hold the mic → confirm exactly one waveform + one Cancel/Confirm pair
   appears (not two overlapping "Recording…"/"Listening…" surfaces as in the original
   bug report screenshots).
4. Release → confirm the review state (Cancel X / Confirm ✓) appears once; Confirm sends,
   Cancel discards; either returns the control cleanly to idle.
5. Switch to **Continuous** mode via the same mic menu → confirm the mic icon now acts as
   a listening on/off toggle in place, and that switching modes mid-Push-to-Talk-capture
   is blocked with a tooltip explanation (unchanged guard, carried over from
   `VoiceControlBar`).
6. Confirm two distinct controls are never confused with one another: the mic icon
   itself mutes/unmutes the *microphone* in Continuous mode (FR-006); a separate,
   always-visible speaker icon mutes/unmutes *Lucy's spoken replies* (FR-006a). Verify
   muting the speaker never affects the mic/listening state.
7. While Lucy is speaking a reply and the speaker icon is unmuted, tap the speaker icon
   → confirm the reply's audio stops immediately AND the icon is now in the muted state
   (FR-006a/b) — a single press does both, there is no separate Stop button anywhere.
   Send another message and confirm Lucy responds in text but stays silent until you tap
   the same icon again to unmute.
8. While Lucy is speaking, confirm no "Lucy is speaking…" text label appears in the
   composer/voice-control row (FR-013) — her speaking state is already visible via the
   reactive presence indicator ("the sphere") elsewhere on the workspace, which this
   feature does not change and which should already be visibly reacting during this
   check.
9. While the mic is actively capturing (either mode), confirm no "Listening…" text
   label appears next to it (FR-014) — only the mic icon's own pulse/active-state visual
   should indicate it's listening.

## 4. Translate control relocated, message list gets more room (Bug 3 / FR-007, FR-008, SC-004)

1. Open a conversation with enough prior messages to require scrolling.
2. Confirm the translate icon no longer appears in a row above the message list, and
   instead appears in the composer/voice-control row at the bottom, visually distinct
   from the mic and speaker-mute icons.
3. Confirm `ProjectPicker` is still visible in its original position above the message
   list (unchanged per research.md Decision 6) — only the translate icon moved.
4. Compare the visible message-list height before/after (e.g. via browser devtools box
   model on the message-list container) → confirm it increased.
5. Click the relocated translate icon → confirm it still translates the last response,
   identically to its prior behavior.

## 5. Real-time hub connections succeed (Bug 4 / FR-009–FR-011, SC-003)

1. Open browser devtools → Network → WS/EventSource filter.
2. Trigger any feature backed by a hub — e.g. open a floating panel (`/hubs/panels`),
   start a workflow run (`/hubs/workflow-execution`), or upload a document for
   processing (`/hubs/document-processing`).
3. Confirm the WebSocket connection to the relevant `/hubs/<name>` path succeeds (HTTP
   101 Switching Protocols, then an open WS connection) — no fallback to SSE, no
   `EventSource... MIME type ("text/html")` console error, no
   "Error parsing handshake response" console error (the exact symptoms from the
   original production log).
4. Repeat for at least one other hub (e.g. `/hubs/memory` or `/hubs/agent-execution`) to
   confirm the fix is uniform, not panel-specific (FR-011).
5. Confirm ordinary SPA client-side routing still works (navigate to a few in-app routes
   directly by URL, refresh) and that static assets still load correctly (no regression
   in the untouched static-file-serving code path, research.md Decision 7).
6. Confirm a genuine connection failure is visible, not silent (FR-010): with the frontend
   running, stop the backend briefly while the viewer/memory/document-workspace pages are
   open. Confirm `ViewerSurface`, `MemoryCenterPage`, and `DocumentWorkspacePage` each show
   a "Live"/"Reconnecting" indicator reacting to the drop (not nothing) — this exercises the
   `isLive` fix to `useFloatingPanelHub`/`useMemoryNotificationsHub`/`useNotificationHub`,
   not just the routing fix in steps 1-5 above.
