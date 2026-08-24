# Data Model: Transcription Crash Fix, Review-Gesture Restoration & Continuous Voice View

No persisted entities, database schema, or migrations are introduced by this feature. This is a
request-handling fix, an interaction-gesture restoration, a logging-infrastructure fix, and a new
UI view composed from existing hooks/components — no new data shape.

## Non-entities (explicitly out of scope)

- The transcription upload's `IFormFile`/`Stream` is unchanged transient request data — this
  feature adds a validation guard, not a new shape.
- Log files (Decision 2) are operational infrastructure, not application data — not modeled,
  migrated, or exposed via any API.
- `isAwaitingTapReview` (Decision 3) is transient, per-render local component state in
  `ChatComposer.tsx` — not persisted, not part of `useVoiceRecorder`'s own `RecordingPhase`.
- The dedicated voice view's "active" flag (Decision 6) is transient local UI state in
  `ChatPage.tsx` — explicitly not the persisted `conversationMode` preference, and not stored
  anywhere beyond the current page session.
