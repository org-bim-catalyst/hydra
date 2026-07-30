# Research: Immersive 3D AI Workspace

**Date**: 2026-07-30 | **Feature**: [spec.md](./spec.md)

This feature is frontend-only: it reshapes the `/chat` route's presentation in
`AskLucy.Web/ClientApp` and introduces no new backend endpoints, entities, or database
changes (confirmed against `src/AskLucy.Web/ClientApp/src/features/chat` and its API/hook
layer, `src/AskLucy.Web/ClientApp/src/routes/router.tsx`). All open technical questions
below are frontend architecture/library decisions.

## 1. 3D rendering library

**Decision**: `@react-three/fiber` (React Three Fiber) + `@react-three/drei`, layered
over `three` as the underlying WebGL engine.

**Rationale**: R3F renders the scene as a declarative React component tree, which fits
this codebase's existing React 19 + component-per-concern conventions (constitution §2.II
SOLID, §2.VI Separation of Concerns) far better than an imperative `three.js` scene
wired up inside a single `useEffect`. `drei` supplies battle-tested helpers this feature
needs directly: `OrbitControls` (manual rotate/zoom/pan, FR-003), `PerformanceMonitor`
(adaptive quality, FR-020/SC-010), and first-class `Suspense` integration (loading
sequencing, FR-021/SC-011). It is the de facto standard for React + WebGL and keeps the
3D scene testable in isolation from the rest of the tree.

**Alternatives considered**:
- Raw `three.js` in a `useEffect`/imperative canvas ref — rejected: fights React's
  declarative model, harder to keep the scene's state (idle vs. reactive vs. degraded)
  in sync with component state without hand-rolled glue.
- Babylon.js — rejected: heavier, no idiomatic React binding, no existing convention in
  this codebase to build on (constitution §2.VII Convention Over Configuration).

## 2. Sphere geometry and deformation

**Decision**: An `IcosahedronGeometry` at a moderate subdivision level, displaced along
each vertex's normal by a time-varying simplex-noise field evaluated in a GLSL vertex
shader (via `drei`'s `shaderMaterial`), with two named parameters — **amplitude** and
**frequency** — driven from component state each frame.

**Rationale**: Shader-side displacement runs on the GPU every frame regardless of scene
complexity, which decouples the sphere's animation from the main JS thread that also
drives the assistant panel's UI — required by FR-017 ("MUST NOT block, freeze, or delay"
the panel) and SC-010. CPU-side per-vertex `BufferGeometry` attribute updates were
considered and rejected as the default path: at a vertex count dense enough to look
organic, per-frame CPU updates risk exactly the main-thread contention FR-017 forbids.
A CPU-side, lower-vertex-count version remains the designated fallback tier for
low-end/degraded mode (see §4).

## 3. Driving the sphere's reaction to voice output (FR-018/FR-019)

**The technical constraint**: `useTextToSpeech.ts` (existing) plays audio via the
browser's native `window.speechSynthesis` (`SpeechSynthesisUtterance`). This API does not
expose the synthesized audio as a `MediaStream` or `AudioBuffer` — there is no
cross-browser-reliable way to attach a Web Audio `AnalyserNode` to what the browser's TTS
engine is actually saying, unlike audio played through an `<audio>` element.

**Decision**: Approximate reactivity from the utterance's own **timing events**
(`SpeechSynthesisUtterance.onstart`, `.onboundary`, `.onend`, `.onerror`) rather than true
frequency/amplitude analysis. Each `boundary` event (fired per word/sentence, depending on
platform) re-triggers a short, damped envelope pulse; the shader's amplitude parameter
follows that envelope, decaying toward the idle baseline between words. `onend`/`onerror`
returns the sphere to its normal idle rotation (FR-018, edge case: voice output
disabled/unavailable). This is exposed as a small extension to the existing hook (e.g. an
`intensity`/`isSpeaking` value alongside `speak`/`stop`), not a rewrite of its public
contract.

**Rationale**: This keeps the feature scoped to a presentation-layer change, as the spec's
assumptions require, and needs no backend or TTS-provider change. The spec requires the
sphere to *read as* reactive to the assistant's voice (FR-018's "functioning as a live
audio visualizer"), not to be a scientifically accurate spectrogram — an envelope driven
by real utterance timing satisfies that bar.

**Alternatives considered**:
- Real Web Audio `AnalyserNode` against the synthesized voice — would require replacing
  `window.speechSynthesis` with a server-rendered audio file streamed to an `<audio>`
  element. This is a materially larger change (new backend TTS infrastructure/provider
  selection, audio streaming or storage, cost) than a layout redesign warrants, and
  nothing in the spec requires true frequency analysis. **Flagged as a candidate future
  enhancement** — it would also incidentally address the constitution's §7 "Voice output"
  requirement (a consistent persona voice across languages), which the current
  browser-default-voice implementation does not fully satisfy today. That pre-existing
  gap is out of scope for this feature and is formally recorded as
  [ADR-0005](../../docs/adr/0005-defer-tts-voice-persona-fix.md).
- No reactivity, idle animation only — rejected: contradicts the explicit clarification
  answer that defined this feature's core visual identity.

## 4. Adaptive performance and accessibility gating

**Decision**: Three discrete quality tiers, not a continuous LOD system: `full` (GPU
shader displacement, 60fps target), `reduced` (lower vertex count / paused rotation,
triggered by `drei`'s `PerformanceMonitor` on sustained frame-time regression or by
viewport width below the mobile breakpoint), and `static-fallback` (no canvas mounted at
all). `prefers-reduced-motion` is read once via
`matchMedia('(prefers-reduced-motion: reduce)')` and, when set, forces idle
rotation/deformation to a frozen or minimal-motion pose regardless of tier (FR-012).
WebGL2 support is feature-detected at mount; if unavailable (or if `three`/R3F throws
during initialization), the canvas is never mounted and the static gradient background
renders instead (FR-011) — wrapped in a component-scoped error boundary so a rendering
failure cannot blank the assistant panel (constitution §2.VIII No Silent Failures).

**Rationale**: Three named tiers are simple to reason about, test, and QA against SC-005/
SC-010, matching constitution §2.III (KISS/YAGNI — no bespoke continuous LOD algorithm
for a decorative background).

## 5. First-paint sequencing (FR-021/SC-011)

**Decision**: The 3D scene component is `React.lazy`-loaded and `Suspense`-wrapped,
mounted behind a synchronous CSS gradient placeholder that is already themed
light/dark. On the scene's ready callback (R3F's `onCreated`), the canvas cross-fades in
via an opacity transition; the placeholder is removed after the transition completes.

**Rationale**: This satisfies FR-021 and constitution §15 (large dependencies lazy-loaded
behind the feature that needs them) with the same mechanism — the code-split boundary
*is* the loading-state boundary, so there's no separate bundle-splitting task layered on
top of the UX requirement.

## 6. Floating assistant panel styling

**Decision**: MUI `Paper`/`Box` with `backdropFilter: 'blur(Npx)'` and a translucent,
theme-aware background (new tokens under `theme/tokens`, both light and dark variants),
composed with MUI transition primitives (`Slide`/`Collapse`) for expand/collapse, anchored
left via fixed positioning (not MUI `Drawer`, which reserves layout space the immersive
full-bleed canvas must not cede). A persistent MUI `Fab` (round button) toggles it and
carries a `Badge` for the FR-016 unread indicator.

**Rationale**: No `backdropFilter`/glassmorphism pattern exists yet in this codebase
(confirmed by search) — this introduces it as a new, documented style, scoped to
`features/chat` rather than `src/shared` until a second feature needs it, per constitution
§7's "used by at least two features or justified as a foundational primitive" rule.

## 7. Conversation history selector (replacing the permanent sidebar)

**Decision**: Extract `ChatSidebar.tsx`'s existing data/query/action layer — it is already
hook-based (`useSearchChats`, `useDeleteChat`, `usePinChat`, etc. in
`features/chat/hooks/useChats.ts` / `useConversationActions.ts`) — into a compact
`ConversationSwitcher` presented in a MUI `Popover`/`Menu` anchored to a control at the
top of the assistant panel, reusing those hooks unchanged. Only the presentational shell
(fixed 300px permanent column → anchored popover) changes.

**Rationale**: Constitution §2.III (DRY) forbids re-implementing chat-list
fetch/search/mutate logic; the existing hooks already isolate that from presentation, so
this is a wrap, not a rewrite.

## 8. New client-side state ownership

**Decision**: Panel open/collapsed state and "unread while collapsed" live in a new
Zustand store (`assistantPanelStore`), following the existing `store/themeStore.ts`
pattern (including `persist` middleware, so the user's last panel state survives a
reload). Ephemeral, per-mount state (current quality tier, speech intensity) stays as
local component state/refs — it is recomputed every load and has no reason to persist.

**Rationale**: Matches constitution §7 exactly ("Client/UI state... lives in Zustand
stores; server state... lives in TanStack Query and MUST NOT be duplicated into
Zustand").

## Summary of new dependencies

| Package | Purpose |
|---|---|
| `three` | WebGL engine underlying R3F |
| `@react-three/fiber` | React renderer for `three` |
| `@react-three/drei` | `OrbitControls`, `PerformanceMonitor`, shader/material helpers |
| `simplex-noise` | Deterministic noise field for idle/reactive vertex displacement |

All four are lazy-loaded behind the `/chat` route's 3D scene component (§5) and never
enter the application's initial bundle.
