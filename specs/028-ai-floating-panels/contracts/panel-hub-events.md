# Contract: Panel Request Real-Time Events

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decision 2)

A new SignalR hub, `PanelHub` (`src/AskLucy.Infrastructure/Panels/PanelHub.cs`), mirroring
`AgentExecutionHub`/`MemoryHub`/`DocumentProcessingHub` exactly.

## Connection & groups

- Hub route: `/hubs/panels` (matching `/hubs/agent-execution`, `/hubs/memory` naming).
- On connect, the hub adds the caller to a group keyed by their own server-verified user id
  (`ClaimTypes.NameIdentifier`), never a client-supplied id — same as every other hub in this codebase.
  This is also what makes panels private per user (spec FR-023, Clarifications Q5): a `PanelRequested`
  push is only ever sent to `PanelHub.UserGroup(triggeringUserId)`, so no other connection ever receives
  it.
- Unauthenticated connections are rejected (`[Authorize]` on the hub class).

## Server → client events

One SignalR method, invoked via `IPanelNotifier` (Application interface, `Application/Panels`) →
`PanelNotifier` (Infrastructure, wraps `IHubContext<PanelHub>`):

| Hub method | Payload | Fired when |
|---|---|---|
| `PanelRequested` | `{ requestId, typeKey, title, data, position?, contextAssociation? }` (matches `PanelRequest`, data-model.md) | An AI agent/tool (chat post-processing or agent tool execution, spec 020/021) decides a response should be shown as a panel and calls `IPanelNotifier.PanelRequestedAsync(userId, request)`. |

Unlike `AgentExecutionHub`'s `ToolCallCompleted` (which deliberately omits raw tool I/O per spec 020
FR-035), `PanelRequested.data` intentionally carries the full structured panel content — that content
**is** the feature being delivered, not execution telemetry about it (research.md Decision 2).

## What triggers a `PanelRequested` push

Out of scope for this plan to fully wire (no existing "AI decides to show a panel" reasoning step
exists yet in the chat/agent pipeline — spec Assumption: "the AI-side decision of *what* to show ...
is produced by Ask Lucy's existing chat/agent capabilities"). This contract defines the **receiving**
side (`PanelHub` → `floatingPanelStore`) so any future chat-response or agent-tool-call code path can
call `IPanelNotifier.PanelRequestedAsync` without a frontend change. For this feature's own
end-to-end verification (quickstart.md), a minimal, explicitly test-only trigger path is used.

## Client hook

Frontend consumes this via `useFloatingPanelHub.ts`
(`src/AskLucy.Web/ClientApp/src/viewer/panels/hooks/`), mirroring `useAgentExecutionHub.ts`'s shape
(connect once per session, on `PanelRequested` call `floatingPanelStore.openPanel(payload)` — which
performs zod validation, registry lookup, cascade placement, and LRU eviction as described in
data-model.md — rather than each panel type/component subscribing independently).

## Validation & error handling on receipt

Per spec FR-016/FR-017/constitution §2.VIII (no silent failures):

| Condition | Behavior |
|---|---|
| `typeKey` not in `PanelTypeRegistry` | Panel is still added to `floatingPanelStore` with `validationStatus: 'unknown-type'`; `FloatingPanel.tsx` renders a visible "Unsupported panel type" fallback instead of a blank/missing panel. |
| `data` fails the resolved type's zod schema | Panel is added with `validationStatus: 'invalid'`; renders a visible "This panel's data couldn't be loaded" fallback with the zod issue summary in a collapsible details section (dev/support diagnosis), never silently dropped. |
| Valid | Panel renders normally via the resolved `PanelTypeDefinition.renderer`. |

A `PanelRequested` push is never simply ignored — every received request results in exactly one
`FloatingPanel` entry in one of the three states above.
