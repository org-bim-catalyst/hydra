# Data Model: Hold-to-Talk Simplification & Self-Listening Fix

No persisted entities, database schema, or migrations are introduced by this feature (confirmed in
spec.md's Key Entities section). This is an interaction-gesture fix, an error-classification
extension, and an audio-capture behavior change — no new data shape.

## Non-entities (explicitly out of scope)

- The Push-to-Talk recording (`Blob`/`File`) is unchanged transient, in-memory, request-scoped
  data — this feature changes only *when* it's finalized (on release, always), not its shape.
- `VoiceStateName` (`useVoiceState.ts`) is existing client-side runtime state. This feature stops
  *setting* the `'Interrupted'` value (Decision 4) but does not remove it from the type union or
  add a new state value.
- The microphone `MediaStreamTrack`'s `enabled` flag is a transient runtime audio-graph property,
  not a data entity — toggled directly by `useSpeechRecognition.ts`, not stored or synced anywhere.
