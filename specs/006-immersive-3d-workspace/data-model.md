# Data Model: Immersive 3D AI Workspace

**Feature**: [spec.md](./spec.md) | See [research.md](./research.md) for rationale.

## No backend/domain changes

This feature is a presentation-layer redesign of the existing `/chat` route. It
introduces **no new domain entities, no new database tables/columns, and no new API
contracts**. It reads and writes conversations and messages exactly as today via the
existing `features/chat/api` and `features/chat/hooks` layers (`useSearchChats`,
`useChatMessages`, `useChatStream`, `useDeleteChat`, `usePinChat`, etc.). Nothing below
requires an EF Core migration or touches `AskLucy.Domain`/`AskLucy.Application`/
`AskLucy.Infrastructure`/`AskLucy.Persistence`.

## New client-side state

All of the following are frontend-only, in-browser state — not persisted server-side.

### AssistantPanelState (Zustand store, `persist`-backed — survives reload)

| Field | Type | Notes |
|---|---|---|
| `isOpen` | `boolean` | Whether the floating assistant panel is expanded. Defaults to `true` on first visit (spec Assumptions). |
| `hasUnreadWhileCollapsed` | `boolean` | Set when an assistant message arrives while `isOpen` is `false`; drives the FR-016 toggle-button indicator. |

**Transitions**: `open()` → `isOpen: true`, `hasUnreadWhileCollapsed: false`. `close()` →
`isOpen: false`. `toggle()` flips `isOpen` and clears `hasUnreadWhileCollapsed` when
opening. `markUnread()` — called only when a new assistant message completes while
`isOpen` is `false` — sets `hasUnreadWhileCollapsed: true`.

### SceneQualityTier (local component state, not persisted)

Enum: `'full' | 'reduced' | 'static-fallback'`. Recomputed on every mount from: WebGL2
feature detection, `prefers-reduced-motion` media query, viewport width, and
`PerformanceMonitor` frame-time sampling (research.md §4). Never written to storage —
device/browser capability can change between sessions (e.g., a different monitor, power
mode).

### SpeechActivityState (local/ref state inside the voice-output hook, not persisted)

| Field | Type | Notes |
|---|---|---|
| `isSpeaking` | `boolean` | Mirrors `SpeechSynthesisUtterance` start/end/error lifecycle. |
| `intensity` | `number` (0–1) | Damped envelope value driven by `onboundary` events, decaying toward 0 between words; feeds the sphere shader's amplitude parameter (research.md §3). |

This extends the existing `useTextToSpeech` hook's return shape; it does not change its
existing `speak`/`stop`/`isSupported` contract.

## Reused existing entities (unchanged)

For reference only — these are documented in prior specs and are not modified here:

- **Conversation** (`ConversationSummary`/chat) — title, pin/favorite/archive flags,
  timestamps; listed via `useSearchChats`, mutated via the existing
  `useConversationActions` hooks. The conversation selector (research.md §7) reads/writes
  these exactly as `ChatSidebar` does today.
- **Message** — role, content, streaming state; loaded via `useChatMessages`/
  `useChatStream`. Rendering moves from a permanent column to the floating panel; the data
  shape and API calls are untouched.
