# Contract: Turn Outcome

**Feature**: `068-honest-retry-offer-card` | **Status**: Design

Defines the recorded outcome of a conversational turn, how it reaches the client, and the claim gate that enforces it. Companion to specs/045's [turn-stream.md](../../045-conversational-agent-runtime/contracts/turn-stream.md).

## 1. Stream event — `__TURN_OUTCOME__`

A new sentinel-prefixed trailing event, following the existing `__RAG__` / `__MEMORY__` / `__LOCATION__` / `__ACTIONS__` convention. Emitted once per assistant turn, before `[DONE]`.

```
data: __TURN_OUTCOME__{"verdict":"Acted","attempts":[...],"failureReason":null,"recordedAtUtc":"2026-09-23T10:14:02Z"}
```

### Payload

| Field | Type | Notes |
|---|---|---|
| `verdict` | string | `AnsweredInWords` \| `Acted` \| `FailedBeforeCompleting`. Serialized as a string, per the API-wide enum converter. |
| `attempts` | array | Zero or more attempts; see below. |
| `failureReason` | string \| null | Required when `verdict` is `FailedBeforeCompleting` or any attempt failed. |
| `recordedAtUtc` | string | ISO-8601 UTC. |

### `attempts[]`

| Field | Type | Notes |
|---|---|---|
| `kind` | string | Capability kind. |
| `key` | string \| null | Capability key where parameterised. |
| `targetLabel` | string \| null | Human-readable target, for display and for the routing summary. |
| `succeeded` | bool | The action's own reported result. **The only source for any success statement.** |
| `failureReason` | string \| null | Required when `succeeded` is false. |

`argumentsJson` is **not** included in the stream payload. It exists server-side for replay only; emitting it would leak resolved internals to the client and invite the client to send them back, which Decision 4 explicitly forbids.

### Emission guarantees

- **Every** assistant turn emits exactly one `__TURN_OUTCOME__`, including turns that fail mid-stream (FR-004c). The mid-stream catch emits it immediately before the existing failure notice and `[DONE]`.
- The event is emitted **after** the outcome is persisted, so a client that receives it can rely on it surviving a reload.
- If persistence fails, the turn surfaces that failure rather than emitting an outcome it did not store (FR-004d).

## 2. Persistence

Serialized into `Message.TurnOutcomeJson` in the same `AppendMessageCommand` transaction as the assistant message. See [data-model.md](../data-model.md) §1–2.

On reload, the transcript endpoint returns `turnOutcome` on each assistant message using the same payload shape as the stream event, so the client has one shape to parse (SC-001c).

## 3. Claim gate

Sits at the single content-delta write site. Governs what reaches the wire.

### Behaviour

| Input sentence | Recorded outcome | Result |
|---|---|---|
| No action claim | any | Released immediately, unchanged |
| Action claim | a matching attempt with `succeeded: true` | Released unchanged |
| Action claim | a matching attempt with `succeeded: false` | **Withheld**, replaced with a correction naming the failure reason |
| Action claim | no matching attempt | **Withheld**, replaced with a claim-free statement |
| Action claim | outcome unavailable | **Withheld** (FR-002c) — never released unverified |

### Guarantees

- **Buffering is bounded to one sentence.** A sentence with no action claim is never held past its terminating boundary (SC-001b).
- **Replacement is never empty.** A withheld sentence is always replaced by an accurate statement, not dropped, so the user is never left with a truncated reply (FR-002b).
- **Correct replies pass untouched.** A claim consistent with the recorded outcome is released byte-identical, protecting against false positives (the spec's "verification disagrees with a correct reply" edge case).
- **No provider dependency.** The gate is deterministic and runs when the provider is down — the condition that produced the original defect.

### Ordering with voice output

Voice output consumes the gated stream, not the raw one, so a withheld sentence is never spoken. Sentence granularity matches the existing sentence-by-sentence TTS segmentation, so no new text/audio desync is introduced.

## 4. Routing summary

Supplied to the turn router alongside the latest user message (FR-009).

```
Recent turns:
  1. set-location  target="Al Safa Park 2"  failed: provider unavailable
  2. (answered in words only)
```

- At most 3 lines, constant regardless of conversation length (FR-009a).
- Projected from recorded outcomes only, never from message prose (FR-009b).
- **Omitted entirely** when there are no recorded outcomes, leaving the prompt byte-identical to today's (FR-009c).

## 5. Audit trail

Every turn is additionally recorded in the existing `AgentExecution` trail (FR-004e), including turns that answer in words and turns that fail before completing. The recording call must be reachable from the mid-stream failure path, which today it is not.

It remains advisory (FR-004f): a write failure is logged and surfaced to operators without failing or delaying the user's turn, and it is never the source consulted for reply composition, verification or retry.
