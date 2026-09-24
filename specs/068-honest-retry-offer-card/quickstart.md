# Quickstart: Verifying Spec 068

**Feature**: `068-honest-retry-offer-card` | **Date**: 2026-09-23

How to reproduce both defects and confirm the fixes. The three user stories are independently testable — each section stands alone.

## Prerequisites

- `PERSISTENCE_TESTS_CONNECTION_STRING` set (value in `appsettings.Development.json`). Without it almost every Web.Tests case fails at Hangfire startup. Point it at the shared test DB, not LocalDB — LocalDB cannot run the full-text migrations.
- Do **not** run persistence tests without `PERSISTENCE_TESTS_DEDICATED_DATABASE=1`; they have wiped the dev DB before.
- Frontend type-checking uses `tsc -b --noEmit`. A bare `tsc --noEmit` silently checks nothing in this repo.

## Reproducing Defect 1 before the fix

The trigger is an AI provider failure on the turn router. Reproduce it without touching production credentials by making the router's provider call fail — a test double that throws on `StreamChatAsync` reproduces the whole chain, since `TurnDecider` catches, degrades, and hands the same dead provider to the fast path.

1. Ask for a workspace action: *"Show me Al Safa Park 2"*.
2. Observe the failure notice: *"Something went wrong partway through and I couldn't finish. Please try again."*
3. Restore the provider.
4. Send *"try again"*.
5. **Before the fix**: Lucy replies with a success claim while the viewer has not moved.

## US1 — Lucy never claims an action she did not perform (P1)

| # | Step | Expected |
|---|---|---|
| 1 | Run the reproduction above through step 5 | No reply anywhere in the conversation asserts the action succeeded |
| 2 | Inspect the failed assistant message's outcome | `verdict: "FailedBeforeCompleting"` with a non-empty `failureReason` |
| 3 | Reload the page, re-read the transcript | The outcome is still attached to the same message (SC-001c) |
| 4 | Send an ordinary conversational message ("what can you do?") | Streams with no perceptible added delay; `verdict: "AnsweredInWords"`, empty attempts (SC-001b) |
| 5 | Force a turn that resolves a location then fails outlining its boundary | Reply credits the location and reports the boundary failure separately — not one verdict for both (FR-006) |
| 6 | Force a turn that genuinely succeeds | Confirmation passes through byte-identical; no correction applied (false-positive check) |
| 7 | Make the outcome unretrievable for a turn | Reply makes no action claim at all rather than an unverified one (FR-002c) |
| 8 | Query the audit trail for every turn above | All appear, including the answered-in-words turn and the one that failed before completing (SC-001d) |

**The claim gate's own tests** should be unit tests, not end-to-end — it is deterministic and needs no provider. Cover: claim-free sentence released immediately; claim + matching success released unchanged; claim + recorded failure withheld and replaced; claim + no outcome withheld; replacement never empty.

## US2 — "Try again" actually tries again (P2)

| # | Step | Expected |
|---|---|---|
| 1 | Fail a location request, restore the provider, send *"try again"* | The original action re-runs with the **original** target; viewer moves |
| 2 | Repeat with *"retry that"* and *"can you try once more"* | Same routing outcome (SC-010) |
| 3 | Leave the provider dead and retry | Retry failure is stated, distinguishable from the first notice — not the same sentence again (FR-011, SC-005) |
| 4 | Use the retry control on the failed message instead of typing | Same re-attempt; no router involvement |
| 5 | Check the transcript after a retry | A **new** assistant turn appended; original failure still visible above; **no** user message inserted (FR-013a/b, SC-012) |
| 6 | Reload | Both turns carry retrievable outcomes (SC-012) |
| 7 | Retry a turn that succeeded | Refused — told it already succeeded, or asked whether to run it again (FR-015) |
| 8 | Fail two different actions, then say *"try again"* | Asks which, or retries the most recent **and names it** (FR-012) |
| 9 | Retry an action whose target no longer resolves | Visible failure with the reason — never an apparent success against a different target |
| 10 | Retry a turn that only answered in words | Re-answers or asks what to retry; does not invent an action |

**Security check**: confirm a `RetryRequest` naming a message in another user's chat resolves as not found, and that no capability kind or arguments are accepted from the client.

## US3 — Readable offer card, not read aloud in full (P3)

Follow these mechanically and compare against the stated expectation.

| # | Step | Expected value |
|---|---|---|
| 1 | Trigger an offer with a long question and long option descriptions, docked panel at default width | Card spans the panel's full usable width |
| 2 | Count words per line on each option label and on the Choose button | No line under 4 words where the text is longer than 4 words (SC-006) |
| 3 | Look at one option row | Radio, label and description on the same horizontal band (FR-018) |
| 4 | Look at the Choose button | Fully visible, label on one line (FR-019) |
| 5 | Find an adjacent assistant message without an offer in the same transcript | It renders at the ordinary reply width, not full width (SC-006a) |
| 6 | Switch docked ↔ expanded, then drag the panel to its narrowest supported width | No clipping, no horizontal scrolling; degrades to a stacked layout (SC-007) |
| 7 | Scroll to an already-answered offer in history | Same width behaviour, still non-interactive (FR-020) |
| 8 | Tab through the card at both widths | Focus order and labelling unchanged (FR-022) |
| 9 | Run the automated accessibility checks | No regression against current results (SC-008) |
| 10 | Enable voice, trigger an offer | Speaks the reply plus a short cue; does **not** recite the question, labels, descriptions or confirm action (SC-011) |
| 11 | Trigger an offer while voice is mid-sentence | Cue is appended, not interrupting |
| 12 | Inspect the card with a screen reader | Full question, options and descriptions all reachable (FR-025) |

## Test suites

```bash
dotnet test tests/AskLucy.Application.Tests      # claim gate, outcome model, routing summary
dotnet test tests/AskLucy.Web.Tests              # stream event, retry endpoint, error mapping
dotnet test tests/AskLucy.Persistence.Tests      # TurnOutcomeJson round-trip  (needs the env vars above)
cd src/AskLucy.Web/ClientApp && npm test         # card width, voice scope, retry affordance
```

Run the **full** frontend suite, not just the touched files — `ChatPage.test.tsx` carries its own assertions about components it renders, independent of those components' own test files. Those tests are also known to be flaky under full parallel load (5s timeouts, "Axe is already running"); a failure that passes in isolation is likely that, not this feature.
