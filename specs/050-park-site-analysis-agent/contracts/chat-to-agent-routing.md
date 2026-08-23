# Contract: Chat-to-Agent Turn Routing

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decision 1)

This is the one genuinely new mechanism this feature introduces (Clarifications Q1). It defines the
boundary between the existing Chat Engine's message-send pipeline and the existing Agent Engine's
`StartAgentExecutionCommand` — the only two things it is allowed to touch.

## Where it runs

`SiteAnalysisChatTurnRouter` (`Application/SiteAnalysis/Routing/`) is invoked from the existing
`SendMessage` command handling pipeline (the same handler every ordinary chat message already goes
through), as an additional step — **not** a new controller endpoint, **not** a new SignalR hub, and
**not** a modification to `AgentPlanner`/`AgentExecutionOrchestrator` themselves.

## Trigger conditions

The router only acts when **both** are true:
1. The message's `UserChat` has a `SiteAnalysisProjectLink` (data-model.md) — i.e., this feature is
   already active for this conversation. A conversation with no link is never intercepted by this
   router; it behaves exactly as an ordinary chat conversation.
2. The message matches one of this feature's narrow intents:
   - **Site description** (only meaningful before a boundary is resolved in this conversation) — a
     place name or coordinates (FR-001).
   - **Category request** — "analyze recreation" / "score social" / equivalent phrasing naming one of
     the two in-scope categories (FR-010).
   - **Bootstrap confirmation** — an explicit yes/confirm reply to the assistant's "create a Project?"
     offer (FR-001e/FR-001f) — only meaningful mid-bootstrap-flow, tracked via the conversation's own
     recent message history, not a new persisted state machine.

Any other message in a linked conversation (small talk, unrelated questions) is **not** intercepted —
it flows through the ordinary chat completion path untouched (User Story 5's "unrelated question does
not interfere" requirement).

## What happens on a match

1. The router assembles an **objective string** for the new execution: the qualifying message's
   intent, plus a short, structured summary of already-known conversation state — the resolved
   `SiteBoundary` (from the most recent successful `resolve_site_boundary` `AgentToolCall` in this
   `UserChat`'s execution history, data-model.md's "Explicitly Not Modeled" note), and which
   categories already have a result (from prior `AgentExecution.FinalOutputJson` rows in this chat).
2. It calls the existing `StartAgentExecutionCommand`:
   - `AgentId` = the one pre-published Site Analysis Agent (research.md Decision 2).
   - `ConversationIntegrationMode` = `ExistingConversation`.
   - `UserChatId` = the current chat's id.
   - `Objective` = the string from step 1.
   - `IsTestExecution` = `false`.
3. The command's existing behavior (spec 020) takes over from there — planning, tool calls, approval
   gating, completion — completely unmodified.

## What happens on completion

`SiteAnalysisAgentExecutionCompletionHandler` (Application) observes this `AgentExecution` reaching
`Completed` (via the existing `AgentExecutionCompleted` domain event, spec 020 — no new completion
mechanism) and:
1. If the execution's objective was a boundary resolution: dispatches an Immersive Viewer command to
   render the boundary (research.md Decision 11) — no new viewer command, reuses the existing
   content-layer API.
2. If the execution's objective was a category scoring request: reads `FinalOutputJson`
   (contracts/site-analysis-category-result.md), calls
   `ITheDigitalCoreClient.RelayCategoryScoreResultAsync` (FR-026), and calls
   `IPanelNotifier.PanelRequestedAsync` with `typeKey = "site-analysis-category-result"`
   (research.md Decision 10).
3. If the execution's objective was a bootstrap confirmation: calls
   `ITheDigitalCoreClient.CreateProjectAsync` and writes the `SiteAnalysisProjectLink` row
   (`LinkSource = BootstrapCreated`), or — if the search step already found a match earlier in the
   same flow — writes it with `LinkSource = BootstrapMatched` without ever calling `CreateProjectAsync`
   (FR-001d).

## Explicit non-goals (guards against scope creep — matches Post-Design Constitution Check note 2)

- This router does **not** become a general-purpose chat intent classifier. It has no trigger
  condition that fires outside a `SiteAnalysisProjectLink`-bearing conversation.
- This router does **not** gain new trigger phrases for the deferred categories
  (Environmental/Sustainability/Accessibility/Safety/Smart City) or for design-concept/report
  generation — a message naming one of those is explicitly routed to a "not yet supported" reply
  (edge case in spec.md), not silently ignored and not added as a new trigger here.
- This router is not itself an `IAgentTool` and is never registered as one — it sits strictly on the
  Chat Engine side of the boundary, calling into the Agent Engine only through the same
  `StartAgentExecutionCommand` any other caller would use.
