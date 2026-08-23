# Quickstart: Validating Composer & Panel Layout Refinements

## Prerequisites

- `src/AskLucy.Web/ClientApp` dependencies installed (`npm install`, if not already).
- Dev server runnable via this repo's existing `run` workflow (backend + Vite dev server), so the
  chat widget can be exercised in a real browser — this feature is layout/DOM behavior that jsdom
  component tests alone cannot fully validate (research.md Decision 2).

## Automated checks

From `src/AskLucy.Web/ClientApp`:

```bash
npm run test -- ChatComposer ExpandedChatPanel chatPanelSizeStore
npm run test -- ChatComposer.a11y ExpandedChatPanel.a11y
npx tsc --noEmit
```

Expected: all pass, no new TypeScript errors, no new ESLint violations on changed files.

## Manual validation scenarios (map to spec.md's User Stories)

1. **US1 — resting shape**: Open the chat widget, expand it, leave the composer empty. Confirm
   the composer is a rounded rectangle (not a full pill) with a distinct footer row of buttons
   along the bottom edge, text-entry area above it.

2. **US2 — capped growth + scroll**: Type/paste text with 10+ newlines into the composer. Confirm
   the composer height stops increasing at ~6 visible lines and a vertical scrollbar appears
   inside the text area; confirm the footer row stays fixed at the bottom throughout. Delete back
   down to 2 lines and confirm the composer shrinks back down (no leftover empty space, no
   scrollbar).

3. **US3 — full-height toggle**: With the panel open at its default size, click the new
   resize/toggle button next to the "+" new-chat button. Confirm the panel expands to occupy
   (approximately) the full window height with a small, even margin top and bottom (research.md
   Decision 3) — no clipping, no page-level scrollbar introduced. Click it again and confirm the
   panel returns to its original half-height size.

4. **US3 (persistence)**: Toggle to full-height, reload the page, reopen the panel. Confirm it
   opens at full-height (chatPanelSizeStore.md's localStorage persistence). Toggle back to
   half-height, reload again, confirm it now opens at half-height.

5. **US4 — placement + tooltips**: Confirm the resize/toggle button sits immediately next to the
   "+" button, not next to the collapse arrow. Hover and keyboard-Tab through every icon-only
   button in the composer and the panel header (attach, insert-prompt, mic, mode-switch, mute,
   translate, send, collapse, new-chat, resize/toggle) and confirm each shows a descriptive
   tooltip. Confirm the mic tooltip's text changes between "Start voice input" and
   "Stop voice input" depending on listening state.

6. **Regression pass**: Send a message, attach a file, use both voice modes (Continuous and
   Push-to-Talk, including the hold-vs-tap gesture), mute/unmute, translate the last response —
   all must behave exactly as before this feature (FR-014), only the visual grouping changed.

## Expected outcome

All six scenarios pass with no regressions in the automated composer/panel/voice test suites
carried over from specs/029-fix-chat-widget-bugs.
