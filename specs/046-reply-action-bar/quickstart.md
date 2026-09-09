# Quickstart: Reply Action Bar

## Prerequisites

- `src/AskLucy.Web/ClientApp` dependencies installed (`npm install`).
- Dev server running (`npm run dev` from `ClientApp`, or use the `run` skill) with the API
  backend reachable, so at least one assistant reply can be produced in a chat.

## Manual validation

1. Start a chat and send a message; wait for the assistant reply to finish streaming.
2. **User Story 2 (layout)**: Confirm a small action row appears directly *below* the reply
   bubble, left-aligned, not overlapping the bubble's text — with a Replay icon (if audio isn't
   muted) and a Copy icon, both visibly smaller than the old floating control.
3. **User Story 1 (copy)**: Click Copy. Confirm a brief visible confirmation appears (icon
   swaps to a checkmark / tooltip reads "Copied"), and paste into any text field to confirm the
   pasted text matches the reply exactly.
4. **User Story 1, failure path**: In a browser context where clipboard permission is denied
   (e.g. deny the clipboard permission prompt, or test via the automated suite mocking
   `navigator.clipboard.writeText` to reject), confirm a visible failure indication appears
   (never a silent no-op).
5. **User Story 3 (no regression)**: Re-run the specs/039 User Story 5 acceptance scenarios
   against the relocated control — play, stop mid-playback, restart-from-beginning after stop,
   and starting replay on a second reply while a first is still playing (confirms the first
   stops).
6. Send a user message and confirm no action row (Copy or Replay) ever appears under it.
7. While a reply is still streaming (before it completes), confirm no action row is shown for
   that in-progress reply.

## Automated tests

```bash
cd src/AskLucy.Web/ClientApp
npm test -- MessageBubble
npm test -- MessageBubble.a11y
npm test -- ChatPage
```

Full suite before considering the feature done (see [[page_level_tests_miss_component_changes]]):

```bash
cd src/AskLucy.Web/ClientApp
npm test
npx tsc -b --noEmit
```
