# Quickstart: Immersive 3D AI Workspace

**Feature**: [spec.md](./spec.md) | **Design**: [research.md](./research.md), [data-model.md](./data-model.md)

## Prerequisites

- Backend and frontend dev environment already set up per `CONTRIBUTING.md`.
- New frontend dependencies installed (research.md "Summary of new dependencies"):

  ```sh
  cd src/AskLucy.Web/ClientApp
  npm install three @react-three/fiber @react-three/drei simplex-noise
  ```

## Run it

```sh
cd src/AskLucy.Web/ClientApp
npm run dev
```

Sign in and land on `/chat` — this is the redesigned route (spec Assumptions: only
`/chat` changes; `/settings`, `/profile`, `/admin/*`, `/privacy` are unaffected).

## Validation scenarios

Each scenario below maps to an acceptance scenario in [spec.md](./spec.md); use it as a
manual (or Playwright/E2E) check that the feature works end-to-end.

| # | Scenario | How to check | Maps to |
|---|---|---|---|
| 1 | Full-viewport sphere on load | Load `/chat`; the 3D sphere fills the viewport behind the assistant panel and rotates without input | US1, SC-001 |
| 2 | Manual manipulation doesn't block chat | Drag/scroll on the scene, then immediately type and send a message | US1 AC3, FR-017 |
| 3 | Panel open/close | Click the round toggle Fab; panel expands/collapses; scene keeps animating underneath | US2, FR-006 |
| 4 | Chat still works | Send a message in the panel; response streams in, panel text stays legible over the moving background | US2 AC2, SC-002/SC-004 |
| 5 | Voice reactivity | Trigger a spoken reply (voice output on); confirm the sphere visibly deforms while speech plays and returns to idle at `onend` | US2 AC4, FR-018, SC-009 |
| 6 | Voice off doesn't block visuals or chat | Disable/mute voice output (or use a browser with no `speechSynthesis` voices); confirm the sphere just idles and chat is unaffected | Edge case, FR-019 |
| 7 | Conversation switcher | With 2+ prior conversations, open the selector at the top of the panel, pick one, confirm its history loads | US3, FR-008/FR-009 |
| 8 | Empty conversation list | As a user with zero conversations, open the selector; confirm the empty/start-new state | US3 AC3 |
| 9 | Responsive layout | Resize the viewport across mobile/tablet/desktop breakpoints (or use devtools device emulation); confirm no overlap/clipping/unreachable controls | US4, SC-005 |
| 10 | No-WebGL fallback | Disable WebGL (e.g. `chrome://flags` → Disable WebGL, or devtools GPU emulation) and reload; confirm a static background renders and the panel is fully usable | US4 AC3, FR-011, SC-007 |
| 11 | Reduced motion | Enable "prefers-reduced-motion: reduce" in devtools (Rendering tab) and reload; confirm rotation/deformation stop or minimize | Edge case, FR-012, SC-006 |
| 12 | Low-end degrade | Use devtools CPU throttling (4x–6x slowdown) and confirm the scene steps down a quality tier rather than freezing the panel | Edge case, FR-020, SC-010 |
| 13 | Unread indicator | Collapse the panel, trigger an assistant reply, confirm the toggle Fab shows an unread indicator; opening it clears the indicator | Edge case, FR-016 |
| 14 | Keyboard-only | Using only Tab/Enter/Space (no pointer), open the panel, send a message, open the conversation selector, switch conversations | FR-013/FR-014, SC-008 |
| 15 | First-load sequencing | Hard-reload `/chat` on a throttled connection; confirm the assistant panel/toggle are usable immediately while the sphere cross-fades in afterward | FR-021, SC-011 |

## Automated coverage expected

Per constitution §10, new/changed behavior needs corresponding tests, added during
`/speckit-tasks` implementation:

- Unit tests: `assistantPanelStore` transitions, reduced-motion detection hook, the
  `useTextToSpeech` intensity/envelope extension (no WebGL context required).
- Component tests: `ConversationSwitcher` (reusing existing chat-hook mocks the way
  `ChatSidebar` tests already do), toggle Fab unread indicator.
- Accessibility tests: automated `axe` pass on the redesigned `/chat` route (panel open
  and closed states).
- The 3D canvas itself is not unit-tested (no WebGL in the test environment); its
  testable logic (quality-tier selection, envelope math) is kept in plain functions/hooks
  outside the R3F component tree so it can be tested without a canvas, per research.md §1.
