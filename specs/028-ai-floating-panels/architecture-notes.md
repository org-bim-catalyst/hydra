# Architecture Notes: AI-to-UI Floating Panel Framework

Constitution §13 ("design documentation... lives with its spec"). This is the spec-level pointer +
the two decisions worth recording explicitly (the ADR question, and the known gaps carried out of
this implementation pass) — the code itself is the full reference (`viewer/panels/`).

## Summary

The panel framework is a new sub-package of the existing framework-agnostic `viewer/` package
(`src/AskLucy.Web/ClientApp/src/viewer/panels/`): a type registry (`registry.ts`, developer-registered
`typeKey → renderer + zod schema`) → a session-scoped Zustand store (`store/floatingPanelStore.ts`,
open panels, cascade placement, LRU eviction, viewport clamping, `ViewerEventBus` subscription for
context staleness) → chrome components (`components/FloatingPanel.tsx`/`FloatingPanelHost.tsx`,
`react-rnd`-based drag/resize) → a SignalR transport (`hooks/useFloatingPanelHub.ts` ↔
`PanelHub`/`PanelNotifier` in `AskLucy.Infrastructure/Panels/`) delivering `PanelRequested` pushes. A
second, small backend aggregate (`UserPanelPreference`, following `UserVoicePreference`'s exact
Domain→Application→Persistence→Web shape) persists the opacity preference, surfaced through a new
Settings "Viewer" tab.

## ADR decision for the `PanelHub`/registry pattern

Constitution §17 requires an ADR when a decision "introduces a new architectural pattern not already
established in the codebase." Evaluated and **declined**: `PanelHub`/`PanelNotifier` is a direct
structural mirror of `AgentExecutionHub`/`AgentExecutionNotifier` (per-user SignalR group, hub in
`Infrastructure`, notifier interface in `Application/Abstractions`) — the same shape this codebase
already uses for `MemoryHub`, `DocumentProcessingHub`, `RetrievalIndexingHub`, and
`WorkflowExecutionHub`. The panel type registry (`typeKey → renderer + schema`, register-once) is a
Strategy/plugin registry — the same shape as the backend's `IAIProvider` (four keyed providers) and
the frontend's own `viewer/api/layers.ts` `RenderLayer` model. Neither is a new pattern; both are
this codebase's already-established patterns applied to a new feature. No ADR filed.

## Known follow-ups (not blocking this feature's completion)

- **Live verification gap**: like spec 027, this environment has no running backend + authenticated
  browser session, so `tests/AskLucy.E2E.Tests/AiFloatingPanels.spec.ts` and quickstart.md's manual
  scenarios have not been runtime-verified end-to-end. Every layer was independently verified instead:
  backend build + full test suite (1538 tests, Domain/Application/Infrastructure/Web, all passing),
  frontend `tsc -b`/`eslint`/`vitest` (all passing), a production `npm run build`, and a direct
  `curl` against a locally-started instance's `/openapi/v1.json` confirming
  `GET`/`PUT /api/v1/panels/preferences` are correctly discoverable. Run the E2E spec and
  quickstart.md scenarios against a real deployment before considering this feature fully verified.
- **Keyboard resize gap**: `react-rnd`'s resize handles are pointer/touch-only, matching that
  library's own accessibility posture (no built-in keyboard equivalent). Dragging got a keyboard
  fallback (arrow-key nudge on the focused title bar, `Shift`+arrow for a larger step); resizing did
  not — a keyboard-only user can fully read and interact with every panel's content but cannot resize
  a resizable panel without a pointer. If this needs closing, the likely shape is a small
  "increase/decrease size" control pair rather than trying to make `react-rnd`'s own handles focusable.
- **`AI decides to show a panel` step**: out of scope for this feature by spec Assumption — `PanelHub`
  defines the receiving contract (`contracts/panel-hub-events.md`), but nothing in the chat/agent
  pipeline yet calls `IPanelNotifier.PanelRequestedAsync`. A future feature wires an actual trigger.
