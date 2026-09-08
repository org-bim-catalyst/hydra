# Contract: Turn Stream

**Feature**: 045-conversational-agent-runtime | **Layers**: `Application/Ai/Commands/SendChatMessage` → `Web/Controllers/v1/AiController` → `ClientApp/features/chat/api/aiApi.ts`

Extends the existing SSE contract. Every current event keeps its name, shape and ordering guarantee; one event is added.

---

## 1. Decision document (internal, model-facing)

The decide step's single completion returns exactly this JSON. Parsed by `TurnDecisionParser` with one corrective retry, reusing `AgentPlanner`'s idiom (research.md D3).

The prompt shows the model the **Tier 1 capability index only** — key, what it does, when to use it, argument hint. It never shows `InputSchemaJson` (research.md D13), so the model cannot shape arguments around the schema the grounder validates against.

```json
{
  "intent": "act",
  "slices": [
    {
      "subAgentKey": "lucy.site",
      "capabilityKey": "resolve_location",
      "arguments": { "query": "Al Safa Park 2" },
      "pendingLabel": "Finding Al Safa Park 2",
      "dependsOn": null
    }
  ]
}
```

There is **no `acknowledgement` field**. The acknowledgement is templated from the selected capability's `AcknowledgementTemplate` (research.md D15), so the first thing the user sees is never behind this call.

| Field | Rules |
|---|---|
| `intent` | `"answer"`, `"act"` or `"suggest"`. Anything else → treated as `"answer"` and logged (degraded, not failed). `"suggest"` (research.md D18) means *answer in text, then offer* — the user asked **about** something rather than asking for it to be done. Borderline cases must choose `"suggest"` over `"act"`. |
| `flowKey` / `flowVariantKey` | Set instead of `slices` when the decision selects a whole flow. With `intent: "act"` the variant runs; with `intent: "suggest"` its variants are offered instead (FR-051a). |
| `slices` | `[]` when `intent = "answer"`. 1–`MaxDelegationsPerTurn` (default 3) otherwise. |
| `slices[].capabilityKey` | Must be in the turn's available set, else the slice is dropped and logged (FR-014). |
| `slices[].arguments` | Must validate against the capability's `InputSchemaJson`, else dropped and logged. |
| `slices[].pendingLabel` | ≤60 chars; names the work for FR-005. Absent → the capability's `Label` is used. |
| `slices[].dependsOn` | 0-based index of an earlier slice, or null. A forward or self reference is rejected and the slice runs independently. |

**Every slice dropped** while `intent = "act"` → the turn degrades to a plain reply and logs why. It never produces an empty turn.

---

## 2. Suggested-actions document (internal, model-facing)

Returned by the offer step. Passed through `SuggestedActionGrounder` before it can reach the wire (FR-024, research.md D10).

```json
{
  "question": "What would you like to do next?",
  "actions": [
    { "kind": "flowVariant", "key": "locate_a_place:full", "arguments": {},
      "label": "Focus and outline the site",
      "description": "Find it, centre the map, and outline the site boundary." },
    { "kind": "capability", "key": "search_knowledge_base", "arguments": { "query": "Al Safa Park 2" },
      "label": "Search my knowledge bases",
      "description": "Look for this site in your attached documents." },
    { "kind": "followUp",
      "label": "Give you more information about it",
      "description": "More detail on the park itself." }
  ]
}
```

A `followUp` row carries no key and no arguments — it is composed prose, not a registry lookup (research.md D20), and selecting it can never reach a capability (FR-021c). Action rows are validated absolutely against the registry; follow-ups are checked best-effort for doing-phrasing (FR-024).

The decline option is appended server-side, never requested from the model, so FR-022 cannot be missed.

**This step often does not run at all.** It is skipped — no model call, no `__ACTIONS__` event — whenever a suppression rule applies (FR-025a): the turn took the fast path, nothing is offerable, the user just declined, or the same options were offered and ignored last turn. `"actions": []` is also a valid response when the model judges nothing worth suggesting, and it is honoured rather than padded (FR-025c). A turn ending with no card is the normal case, not a degraded one.

---

## 3. `ChatStreamChunk` — one new field

```csharp
public sealed record ChatStreamChunk(
    string? ContentDelta,
    ChatUsage? Usage,
    RagRetrievalOutcome? RetrievalOutcome = null,
    MemoryRetrievalOutcome? MemoryOutcome = null,
    ConfirmedLocationData? ConfirmedLocation = null,
    ViewerZoomCommand? ViewerZoom = null,
    ConfirmedSiteBoundaryData? ConfirmedBoundary = null,
    bool StartsNewMessage = false,
    string? PendingLabel = null,

    /// <summary>
    /// specs/045 FR-021 — the offer closing this turn, already grounded. Rides the final
    /// chunk exactly as RetrievalOutcome/MemoryOutcome/ConfirmedLocation do. Null when the
    /// turn offered nothing (FR-025).
    /// </summary>
    IReadOnlyList<SuggestedAction>? SuggestedActions = null);
```

`ConfirmedLocationData` and `ConfirmedSiteBoundaryData` are **unchanged** (FR-048).

---

## 4. SSE events

| Event | Status | Shape |
|---|---|---|
| plain `data: <text>` | unchanged | content delta |
| `__MESSAGE_BREAK__{json}` | unchanged | `{ "pendingLabel": "…" }` — the beat mechanism (FR-004/FR-005) |
| `__RAG__{json}` | unchanged | |
| `__MEMORY__{json}` | unchanged | |
| `__LOCATION__{json}` | unchanged | |
| `__ZOOM__<direction>` | unchanged | |
| `__SITE_BOUNDARY__{json}` | unchanged | |
| **`__ACTIONS__{json}`** | **new** | `{ "offeredByMessageId": "<guid>", "question": "…", "actions": [ { "capabilityKey", "label", "description", "arguments", "isDecline" } ] }` |
| `: keep-alive` | **new** | SSE comment, written every 10 s while a beat is pending (research.md D5) |
| `data: [DONE]` | unchanged | |

`__ACTIONS__` is written **after** the assistant message carrying the offer is persisted, so `offeredByMessageId` is a real id the client can echo back on selection — the same reason `__MEMORY__` is ordered where it is.

### Ordering guarantees (preserving specs/044)

1. `__LOCATION__` is written and flushed the moment its chunk is yielded, **before** any optional long-running step — never after (specs/044 FR-001a).
2. A long step is announced by `__MESSAGE_BREAK__` with its `pendingLabel` **before** the step runs (FR-005a).
3. `__SITE_BOUNDARY__` is its own later delivery, never bundled with `__LOCATION__` (specs/044 FR-001b).
4. `__ACTIONS__` is last before `[DONE]`.
5. A failure at any point still reaches `[DONE]` after emitting user-visible text.

---

## 5. Turn beat sequence

**Navigational intent.** "Show me Al Safa Park 2" selects the `locate_a_place` flow, which runs all three steps in **one** turn ([capability-flow.md](./capability-flow.md)):

```text
  __MESSAGE_BREAK__{"pendingLabel":"Looking for Al Safa Park 2"}
  data: Looking for Al Safa Park 2.                     ← step 1 announced (FR-052)
  __MESSAGE_BREAK__{"pendingLabel":"Focusing the viewer"}
  data: Location found. Now focusing the viewer on it.  ← step 1 done + step 2 announced (FR-053)
  __LOCATION__{…}                                       ← viewer moves; flushed immediately (FR-048)
  __MESSAGE_BREAK__{"pendingLabel":"Highlighting the boundary"}
  data: Site focused. Now highlighting the boundary.    ← step 2 done + step 3 announced
  : keep-alive                                          ← every 10 s while Overpass/vision run
  __MESSAGE_BREAK__
  data: Boundary highlighted — about 4.2 hectares,      ← step 3 done; flow complete
        medium confidence from OpenStreetMap.
  __SITE_BOUNDARY__{…}
  data: [DONE]                                          ← often no __ACTIONS__ at all (FR-025a)
```

Three steps, four messages (FR-053). Every step is named once as it starts and once as it ends; none is folded or silent.

Three things to note. `__LOCATION__` is still flushed the instant it exists, **before** the long boundary step — the specs/044 guarantee is unchanged by flows. Step 2 gets no announcement of its own because it finishes faster than one could be read. And the turn frequently ends with no offer: the flow already did the useful follow-ups, so nothing is left that is worth suggesting.

**Informational intent.** "Do you know Al Safa Park 2?" runs nothing and offers the flow's variants instead (FR-051a.2):

```text
  data: Yes — it's a public park in the Al Safa district of Dubai.
  __ACTIONS__{ "actions": [
      { "kind":"flowVariant",   "key":"locate_a_place:focus", … },
      { "kind":"flowVariant",   "key":"locate_a_place:full",  … },
      { "kind":"followUp",      "text":"Give you more information about it", … },  ← composed;
      { "kind":"decline"                                          } ] }              ← runs nothing
  data: [DONE]                                          ← the viewer never moved
```

A mixed list (FR-021a) — two flow variants, a capability-free follow-up composed for this situation, and the decline. Only `decline` is appended server-side. Note the follow-up carries `text`, not a key: it is composed, not registered (research.md D20).

**A stopped flow names the cause only** (FR-056, research.md D19):

```text
  data: OK, let me find it first.
  __MESSAGE_BREAK__
  data: I couldn't find a place matching that name.     ← no "…so I haven't outlined anything"
  data: [DONE]
```

---

## 6. Frontend stream event

```ts
export type ChatStreamEvent =
  | …existing variants unchanged…
  | {
      type: 'actions'
      offeredByMessageId: string
      question: string
      actions: {
        capabilityKey: string
        label: string
        description: string
        arguments: unknown
        isDecline: boolean
      }[]
    }
```

`ChatMessage` gains `suggestedActions?: SuggestedAction[]` and `question?: string`, both populated from `__ACTIONS__` live and from history on reload (FR-026).

**Voice string** (FR-044): `ChatPage` speaks `question + actions.filter(a => !a.isDecline).map(a => a.label).join('. ')`. Descriptions, `capabilityKey` and `arguments` are never passed to `useVoiceOutput`.

---

## 7. Failure matrix (constitution §2.VIII, FR-039–FR-042)

| Failure | Server behaviour | What the user sees |
|---|---|---|
| Decision call throws / unparseable after retry | Log with provider, model and a bounded content prefix; degrade to a plain reply; `AgentExecution` = `Completed` with a `TerminationReason` | The answer, plus "I couldn't work out a plan for that, so here's a direct answer." The acknowledgement is unaffected — it is templated, not returned by this call (research.md D15). |
| Embedding index retrieval fails or the model file is missing | Log once per process; fall back to listing **all** available Tier 1 entries | Nothing — the turn proceeds, at a larger prompt. Retrieval is an optimisation, never a dependency. |
| Slice's capability key ungrounded | Drop the slice, log key + reason | Nothing about that slice; the turn proceeds with the rest |
| **All** slices dropped | Log; degrade to plain reply | A direct answer |
| Capability `ExecuteAsync` returns `Failure` | Record `AgentToolCall` + `AgentExecutionError`; continue other slices | Named failure for that part, results of the others (FR-018/FR-040) |
| Capability throws | Caught by the orchestrator's isolation wrapper (the specs/044 `ResolveBoundarySafelyAsync` pattern); same as above | As above |
| Capability exceeds its budget | Linked-token cancel, not `Task.WaitAsync` — so the shared host's connections are released; recorded as a timeout | "I stopped waiting for X" |
| Turn exceeds `MaxTurnDurationSeconds` | `AgentBudgetGuard` halts; partial results kept | Results so far + "I stopped there." (FR-009) |
| Offer step fails | Log; emit no `__ACTIONS__` | The turn ends normally with no option list (FR-025) |
| Grounded offer fails validation | Drop the whole offer, log | No option list |
| Client disconnects | `OperationCanceledException` re-thrown when the **original** token is cancelled — never mis-recorded as a timeout (specs/044 FR-007); execution `Cancelled` | On reload: the delivered beats, marked interrupted (FR-042) |
| Selection names a stale `offeredByMessageId` | 409 Problem Details, `conversation-action-stale` | Inline error + the current offer stays usable (FR-028/FR-029) |
| Selection names an unavailable capability | 409 Problem Details, `conversation-action-unavailable` | Inline error naming why it is no longer possible |
| Persisting the assistant message fails | Middleware → Problem Details; stream terminated | Visible error, not a silently lost reply |

**Frontend rule**: the selection mutation, the stream reader and the voice call each have an explicit `catch` reaching a visible MUI `Alert` or toast. No `void promise` anywhere in the dispatch path.
