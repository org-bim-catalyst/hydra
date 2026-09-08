# Quickstart: Validating the Conversational Agent Runtime

**Feature**: 045-conversational-agent-runtime | **Date**: 2026-09-08

How to prove this feature works end to end. Every scenario maps to a spec user story and its success criteria. Implementation belongs in `tasks.md`; this file is the run-and-verify guide.

---

## Prerequisites

- .NET 10 SDK; Node 20+
- SQL Server reachable, **migrations applied** — this repository does not migrate at startup:
  ```powershell
  dotnet ef database update --project src/AskLucy.Persistence --startup-project src/AskLucy.Web
  ```
- At least one enabled AI provider with a default model in the catalog (Settings → AI Providers). Nothing needs to be assigned to the new `TurnOrchestration` capability — it falls back to the platform default with a logged warning.
- `Geocoding:GoogleMapsApiKey` in user-secrets if you want location scenarios to behave as they do in production. Without it the app falls back to Nominatim, whose `importance` scale differs and will change which candidate wins.
- **The local embedding model must be on disk**: `src/AskLucy.Web/App_Data/embedding-models/model.onnx` and `vocab.txt` (~90 MB, a MiniLM-class sentence-transformers ONNX export with a `last_hidden_state` output). This directory does not exist in a fresh checkout — `OnnxLocalEmbeddingProvider` has been coded since specs/016 but never deployed. Without it, capability-index retrieval logs once and falls back to listing every capability; the feature still works, just with a larger prompt.
- For backend tests: `PERSISTENCE_TESTS_CONNECTION_STRING` set (value is in `appsettings.Development.json`). Without it almost every `Web.Tests` test fails at Hangfire startup.

## Run

```powershell
dotnet build "Ask Lucy.sln"
dotnet run --project src/AskLucy.Web            # API + SPA
cd src/AskLucy.Web/ClientApp; npm run dev       # optional: Vite dev server with HMR
```

## Test

```powershell
dotnet test "Ask Lucy.sln"                                   # full backend suite
dotnet test tests/AskLucy.Application.Tests                  # orchestrator, capabilities, grounding
cd src/AskLucy.Web/ClientApp
npx tsc -b --noEmit                                          # NOT bare `tsc --noEmit` — that checks nothing here
npm test                                                     # full Vitest suite, not just the touched file
```

Run the **whole** frontend suite: `ChatPage.test.tsx` carries its own assertions about message rendering that a component-level test file will not catch.

---

## Scenario 1 — The narrated flow, run automatically (US1 · US4 · SC-001)

1. Open a new conversation.
2. Send **"Show me Al Safa Park 2"** — a navigational request, so the whole `locate_a_place` flow runs without asking (FR-051a.1).

**Expect, in order, as separate message bubbles** — three steps, four messages (FR-053):

| # | What appears | Verifies |
|---|---|---|
| 1 | "Looking for Al Safa Park 2." — visible **before** any lookup completes | FR-003, FR-052 |
| 2 | A pending indication naming the work — not a bare spinner | FR-005 |
| 3 | "Location found. Now focusing the viewer on it." | FR-053 |
| 4 | The viewer recentres on the park, flushed immediately | FR-048 |
| 5 | "Site focused. Now highlighting the boundary." | FR-053 |
| 6 | "Boundary highlighted — about N hectares…" written from the real result | FR-007 |
| 7 | Usually **no** option card — the flow already did the useful follow-ups | FR-025a |

**Must NOT happen**: a single merged bubble; a step that runs without being announced first; an option card offering the boundary (it is a flow step, not an option — FR-046, FR-060).

**Timing**: message 1 appears no later than the first reply token did before this feature. Compare against `git stash`-ed behaviour or a recorded baseline.

## Scenario 2 — Grounded options (US2 · SC-002)

Use an **informational** request so an offer actually appears (FR-051a.2):

1. Send **"Do you know Al Safa Park 2?"** with **no** knowledge base attached. Note the offered rows.
2. Attach a knowledge base, start a new conversation, repeat.

**Expect**: Lucy answers in text and **does not move the viewer**. The offer mixes kinds (FR-021a) — the `focus` and `full` flow variants, a composed conversational follow-up, and the decline. "Search my knowledge bases" appears only in run 2. Every **action** row (flow variant or capability) maps to a registered, available key; there is no free-text row.

**Negative check**: no row mentions photos or 360° imagery. Confirm in the log a `Warning` naming any discarded key or follow-up text with its reason.

**Forcing the negative path**: temporarily edit `SuggestedActionPrompt` to propose `show_360_photos` as a capability row *and* "Show you 360° photos" as a follow-up. Confirm the first is discarded absolutely (registry miss) and the second by the doing-phrasing check, both logged, neither shown. Revert.

## Scenario 3 — Selection dispatches exactly (US3 · SC-003 · SC-002a)

1. From Scenario 2's card, select **"Focus and outline the site"** and submit.

**Expect**: the full flow runs from step 1 with the same narration as Scenario 1 (FR-051c); no clarifying question.

2. Repeat, selecting **"Focus the viewer on it"** → steps 1–2 only; the boundary is **not** drawn.
3. Repeat, selecting the conversational follow-up → Lucy answers in text; confirm in the turn record that **zero capabilities were invoked** (SC-002a).
4. Repeat, choosing decline → a brief acknowledgement, no work, and no card on the following turn (FR-025a.3).
5. Type a normal message instead of selecting → handled normally, card renders inert (FR-030).
6. Scroll back and select an older card → refused with a visible inline explanation (`conversation-action-stale`).

## Scenario 4 — Intent gating and continuous progress (US4 · SC-004 · SC-004a)

1. Send a **passing mention**: "I read that Al Safa Park was renovated last year."

**Expect**: no flow, no offer, and the viewer does not move (FR-051a.3).

2. Re-run Scenario 1 and watch the conversation for the full duration.

**Expect**: at every moment you can state which step is running and which remain (SC-004). Wall-clock time is **not** expected to beat the pre-feature baseline — the same work is done, narrated rather than silent.

3. Ask for a scoped flow: "just find Al Safa Park 2, don't outline it."

**Expect**: step 1 only (FR-058).

4. Repeat Scenario 1 for a site already outlined.

**Expect**: the boundary step is skipped with a brief note and the flow completes fast (FR-057).

**SC-004a check**: record the session and confirm no gap longer than 5 s without a visible indication. Watch the network panel for `: keep-alive` SSE comments during the boundary step — their absence over a slow lookup means a proxy is buffering and the indication would stall.

**Failure check** (FR-056, D19): request a nonsense place. Lucy names **only** the cause — "I couldn't find a place matching that name" — and does **not** add that the viewer and boundary steps were therefore skipped.

## Scenario 5 — Sub-agent delegation (US5 · SC-005)

1. Attach a knowledge base containing site or planning standards.
2. Send **"Show me Al Safa Park 2 and tell me what our standards say about park setbacks."**

**Expect**: both parts answered in one turn, each narrated as it happens. Confirm in the execution record that two different sub-agents ran (`lucy.site`, `lucy.knowledge`) and that the knowledge slice received the resolved site rather than re-deriving it (FR-017).

**Failure isolation**: disable the knowledge base mid-flight (or point it at an unreachable store) and repeat → the location result still arrives, and the knowledge failure is named (FR-018).

## Scenario 6 — Lucy is inspectable (US6 · SC-006, SC-011)

1. On a **fresh** database, start the app and open Agents.

**Expect**: `Lucy`, `Site Analyst`, `Knowledge Analyst`, `Memory Keeper`, `Viewer Control` present, badged as provisioned by the platform, with no edit or delete affordance. This is the "the agents table is empty" gap closed.

2. Restart the app → no new versions published (idempotent).
3. Attempt a mutation via the API (`PUT /api/v1/agents/{id}` against a system agent) → `403 system-agent-immutable`.
4. Open the execution record for Scenario 5's turn.

**Expect**: the decision document, one step per beat, one tool call per capability invocation with its inputs and outputs, any discarded suggestions with reasons, and token/latency cost attributed to you.

## Scenario 7 — Voice (US7 · SC-010)

1. Enable voice output; run Scenario 1.

**Expect**: each beat spoken as it arrives, not held to the end. The question and option **labels** are spoken; the **descriptions** are not, and no key, argument or JSON is ever spoken.

2. Repeat in a second supported language → same persona, same behaviour.

## Scenario 8 — Nothing fails silently (US8 · SC-007, SC-009)

Force each failure and confirm a visible, in-conversation explanation every time:

| Force it by | Expect |
|---|---|
| Pointing `TurnOrchestration` at a provider with an invalid key | A direct answer plus "I couldn't work out a plan for that" — never an empty reply |
| Blocking the geocoder (firewall / bad key) | Named lookup failure, turn still completes |
| Setting `MaxTurnDurationSeconds` very low | "I stopped there" with partial results kept |
| Closing the browser tab mid-turn, then reopening | Delivered beats present, turn marked interrupted — not shown as complete |

**Replay (SC-009)**: reload any completed conversation from Scenarios 1–5 and confirm the transcript reproduces the beats, the offered options and the selection, in the original order.

## Scenario 9 — The capability index (research.md D13, D14)

1. Run Scenario 1 with debug logging on and inspect the decide prompt.

**Expect**: one compact line per available capability — key, what it does, when to use it, argument hint. **No JSON input schema anywhere in the prompt** (Tier 3 never reaches a model). Roughly 25 tokens per capability.

2. Connect an MCP server exposing more than 10 tools and repeat.

**Expect**: the index is narrowed to the top-N most relevant entries plus every context-gated available capability (a boundary stays listed whenever a location is active, regardless of similarity score). Confirm the prompt size stays roughly constant as the catalogue grows.

3. Rename `App_Data/embedding-models` and repeat.

**Expect**: one logged warning, then the full unnarrowed index. The turn completes normally — retrieval is an optimisation, never a dependency.

4. Check the acknowledgement's origin: it is the capability's `AcknowledgementTemplate`, identical every time for the same capability, and it appears **before** the decide call could possibly have returned.

## Scenario 10 — Preference off (FR-032)

1. Settings → turn suggested actions off.
2. Repeat Scenario 1.

**Expect**: beats and narration unchanged; no option card; no `__ACTIONS__` event in the network panel; no offer model call in the log. Reload an older conversation → its historical offers render as plain text, not interactive rows.

---

## Regression checks

These are the behaviours this feature rewires; confirm none broke.

- **specs/044 ordering** — `__LOCATION__` is written and flushed before any long step, and a boundary failure never takes the location, message persistence or `[DONE]` down with it.
- **specs/037 prompt injection** — geocoder `display_name` is still embedded as data into templates and never re-fed to a model as instruction.
- **specs/042 correctness** — an accepted boundary matches what the old automatic path produced for the same site.
- **Pre-feature conversations** (FR-049) open, render and continue.
- **Background agent runtime** — run an ordinary user-created agent execution; the Hangfire path, approvals and audit are untouched.
- **Concurrency** — under a delegating turn, watch for `DbContext` concurrency errors. Concurrent slices must resolve their own scope; this repository has already shipped that bug once via a shared request-scoped `DbContext`.
