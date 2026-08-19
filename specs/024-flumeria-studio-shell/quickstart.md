# Quickstart: Validating the Flumeria Studio Workspace Shell

## Prerequisites

- Backend (`AskLucy.Web`) running locally with a signed-in test account (existing chat history helps validate US3/edge cases).
- Frontend dev server:

```sh
cd src/AskLucy.Web/ClientApp
npm install
npm run dev
```

- Sign in, then navigate to the workspace.

## Scenario 1 — Renamed, full-viewport workspace (US1)

1. Navigate to `/studio` while signed in.
2. **Expect**: the browser tab title reads "Flumeria Studio"; the placeholder `WorkspaceSurface` fills the entire viewport edge-to-edge; no fixed-position toolbar/sidebar/menu bar is visible — only small circular controls float over the surface.
3. Navigate to the old URL, `/chat`, directly.
4. **Expect**: you land on `/studio` (redirected, not a broken link — FR-002/SC-005).
5. Activate the account circular control.
6. **Expect**: every destination previously reachable from the top-bar account menu (Profile, Settings, Documents, Knowledge Bases, Memory Center, Prompts, Agents, Workflows, Admin panel where applicable, Privacy Policy) is present, the theme toggle works, and "Log out" successfully signs you out (FR-024).

## Scenario 2 — Circular control expand/collapse (US2)

1. From `/studio`, click/tap the view-mode circular control.
2. **Expect**: it expands into a rounded pill/rectangle in place (not a detached dropdown), with a smooth (~300ms) transition; selecting "2D" or "3D" visibly changes the active mode (FR-011); it then collapses back.
3. Click/tap the layers, navigation, selection, and analysis controls in turn.
4. **Expect**: each expands normally and clearly states the capability is "coming soon" (FR-012) — not blank, broken, or unresponsive.
5. Expand one control, then click/tap a different one without collapsing the first.
6. **Expect**: the first automatically collapses; only the newly activated control is expanded (FR-015).

## Scenario 3 — Chat through the same pattern (US3)

1. Click/tap the chat circular control.
2. **Expect**: it expands into the familiar conversation panel (conversation switcher + composer + message list).
3. Send a message.
4. **Expect**: the AI response streams in exactly as it did before this redesign — no functional regression (FR-014/SC-006).
5. Collapse the chat control.
6. **Expect**: the panel closes; the workspace surface and other controls remain fully usable underneath.

## Scenario 4 — Keyboard-only operation (US4)

1. Using only `Tab`, move focus through the workspace.
2. **Expect**: focus visibly lands on each circular control in a predictable order.
3. With a control focused, press `Enter` or `Space`.
4. **Expect**: it expands; `Tab` continues into its revealed content.
5. Press `Escape`.
6. **Expect**: the control collapses and focus returns to its trigger.
7. Run a screen reader (e.g., NVDA/VoiceOver) over the same flow.
8. **Expect**: expand/collapse is announced via `aria-expanded` changes.

## Scenario 5 — Responsive behavior (US5)

1. Resize the browser (or use device emulation) through mobile, tablet, and desktop widths.
2. **Expect**: the surface still fills the viewport at every width; circular controls reposition/resize without overlapping each other or clipping off-screen; an expanded control's content stays fully within the viewport.

## Reduced motion

1. Enable "reduce motion" at the OS level (or emulate `prefers-reduced-motion: reduce` in devtools).
2. Repeat Scenario 2.
3. **Expect**: expand/collapse still happens, but with minimal/instant transitions (FR-018) — this should require no feature-specific code, since it comes from the existing `createMotionTokens` wiring (research.md #2).

## Automated checks

```sh
cd src/AskLucy.Web/ClientApp
npm run test     # vitest — component behavior + workspaceOverlayStore unit tests
npm run lint      # ESLint/Prettier
```

- Every new component under `src/components/workspace-shell/` should have a matching `*.a11y.test.tsx` (jest-axe, zero violations) alongside its `*.test.tsx`, matching the existing convention (see `src/components/EmptyState.a11y.test.tsx` for the pattern).
- `workspaceOverlayStore.test.ts` should cover: expanding a second control collapses the first (FR-015); `markUnread`/`expand` clears the unread flag; no `persist` middleware is wired (session-only, per data-model.md).

## Definition of done for this quickstart

All five scenarios pass manually in a real browser, automated tests pass, and `git grep "'/chat'"` (or the TS equivalent) under `src/AskLucy.Web/ClientApp/src` returns no remaining internal navigation literals — only the `/chat` → `/studio` redirect route definition itself.
