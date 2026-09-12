---

description: "Task list for 049-panel-content-model"
---

# Tasks: Panel Content Model

**Input**: Design documents from `/specs/049-panel-content-model/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. Constitution §10 requires tests for new behaviour in the same change that introduces it, and spec FR-028/SC-004 require the existing specs/028 coverage to keep passing.

**Organization**: Grouped by user story so each is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Paths are repository-relative.

## Path Conventions

- Frontend: `src/AskLucy.Web/ClientApp/src/viewer/panels/`
- Backend: `src/AskLucy.Application/`
- E2E: `tests/AskLucy.E2E.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffolding for the three new areas the plan introduces

- [X] T001 [P] Create directory `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/` with an empty `content/index.ts` barrel
- [X] T002 [P] Create directory `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/`
- [X] T003 [P] Create directory `src/AskLucy.Web/ClientApp/src/viewer/panels/chrome/`
- [X] T004 **GATE — blocks Phase 2.** Confirm `z.toJSONSchema()` in the installed zod 4.1.x produces a stable, deterministic document for a sample discriminated union with the constructs this vocabulary needs (discriminated union, nested objects, optional fields, string/array length limits). The plan's only Complexity Tracking entry is mitigated entirely by this generation, so it must be proven before the vocabulary is written, not discovered during T007. **If it fails** — output is non-deterministic, or it cannot express the needed constructs — fall back to a hand-written `panel-content.schema.json` as the source of truth, with T008 becoming a shared-fixture parity test (a corpus of valid and invalid documents that both the zod schema and the JSON Schema must agree on). Record the outcome and the path taken in `specs/049-panel-content-model/research.md` under D2. **Outcome: gate passed** — zod 4.4.3's `z.toJSONSchema()` is deterministic and expresses every needed construct; no fallback triggered (research.md D2)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The vocabulary, the request shape and the store branch. Nothing in any user story can be built until these exist.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 Define the block vocabulary zod schemas for all eight kinds and the content document wrapper (`version`, `blocks`) in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks.ts`, per contracts/content-vocabulary.md — a discriminated union on `kind`, flat (no nesting), with the field limits from data-model.md. **Every collection must be bounded**: `blocks` ≤ 50, `keyValue.items` ≤ 100, `table.rows` ≤ 200, `table.columns` ≤ 20, `chart.series` ≤ 10. Constitution §7 requires long lists to be virtualized; a bounded row count satisfies the same concern without a virtualization dependency, and an unbounded table composed by a model is the realistic way this surface becomes unusable
- [X] T006 [P] Add the vocabulary version constant (`CONTENT_VOCABULARY_VERSION = 1`) and export it from `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks.ts`
- [X] T007 Generate `specs/049-panel-content-model/contracts/panel-content.schema.json` from the zod source in T005 and commit it
- [X] T008 Add a schema parity test in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks.schema.test.ts` that regenerates the JSON Schema from zod and fails if it differs from the committed artifact (research D2 — this test is the entire drift-prevention mechanism)
- [X] T009 [P] Define `PanelChrome` (`titleBar`, `resizable`, `defaultSize`) and `DEFAULT_CONTENT_CHROME` in `src/AskLucy.Web/ClientApp/src/viewer/panels/chrome/chrome.ts`
- [X] T010 Change `PanelRequest` to a discriminated union on `kind` (`'content' | 'live'`) and update `FloatingPanel` (`typeKey` optional, add `content` and `chrome`, drop the standalone `resizable`) in `src/AskLucy.Web/ClientApp/src/viewer/panels/types/panel.ts`, per contracts/panel-request.md
- [X] T011 Branch `openPanel` on request kind in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.ts` — content requests validate against the vocabulary, live requests keep today's registry resolution — leaving every path after panel construction (cascade, z-order, LRU eviction, minimise/restore, clamping, context association) untouched (research D9)
- [X] T012 Update `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.test.ts` for the new request shape, keeping every existing assertion about cascade, eviction, focus and minimise/restore intact
- [X] T013 Change `PanelRequestDto` to the discriminated content/live shape in `src/AskLucy.Application/Panels/PanelRequestDto.cs`, per contracts/panel-request.md
- [X] T013a Redefine the open-panel signal on `TurnContext` in `src/AskLucy.Application/Conversations/Capabilities/TurnContext.cs`: replace `OpenPanelTypeKeys` with `OpenPanelCount`. The existing field keys the concurrent-panel gate on panel *type keys*, which content panels no longer have, so the gate would silently stop working. Update `TurnContext.Empty`, its XML doc, and every construction site — `TurnContextFactory.cs` and the client-side reporting that populates it
- [X] T013b Update `tests/AskLucy.Application.Tests/Conversations/Capabilities/CapabilityAvailabilityTests.cs` for the `OpenPanelCount` field, replacing the `OpenPanelTypeKeys = ["chart", "table"]` fixtures at the open-panel capacity tests (around L170–L180)

**Checkpoint**: Vocabulary defined, request shape settled, store branches correctly, turn context carries a signal that still means something — user stories can begin

---

## Phase 3: User Story 1 — Lucy Presents Composed Content Without New Code (Priority: P1) 🎯 MVP

**Goal**: Lucy composes any panel content from the block vocabulary; one renderer draws it; presenting something new costs no code.

**Independent Test**: Open panels with several different block compositions (a location, a comparison, a breakdown, a summary) and confirm each renders correctly, with no content-specific code written for any of them.

### Tests for User Story 1

- [X] T014 [P] [US1] Unit tests for the heading and text block renderers in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/HeadingBlock.test.tsx` and `TextBlock.test.tsx`, including that markup in `text` is rendered literally and never interpreted (spec FR-005, constitution §8)
- [X] T015 [P] [US1] Unit tests for the keyValue block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/KeyValueBlock.test.tsx`, including that a `null` value renders an explicit unavailable marker rather than an empty cell
- [X] T016 [P] [US1] Unit tests for the table block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/TableBlock.test.tsx`, including a row whose cell count differs from the column count rendering what is present and being marked malformed without failing the block, and a table exceeding the row cap being rejected by the schema rather than rendered
- [X] T017 [P] [US1] Unit tests for the chart, metric, image and divider block renderers in their respective `*.test.tsx` files under `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/`
- [X] T018 [P] [US1] Unit tests for `ContentRenderer` in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/ContentRenderer.test.tsx` — block order preserved, mixed compositions render, an unknown kind yields a placeholder while siblings render
- [X] T019 [P] [US1] Backend tests for `PresentPanelContentCapability` in `tests/AskLucy.Application.Tests/` covering a valid composition pushing a request, and an invalid composition being refused by the schema gate before any push

### Implementation for User Story 1

- [X] T020 [P] [US1] Implement the heading block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/HeadingBlock.tsx` using MUI typography, theme colours only
- [X] T021 [P] [US1] Implement the text block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/TextBlock.tsx`, preserving line breaks and never using `dangerouslySetInnerHTML`
- [X] T022 [P] [US1] Implement the keyValue block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/KeyValueBlock.tsx`, porting the presentation from `types/parameters/ParametersPanel.tsx`
- [X] T023 [P] [US1] Implement the table block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/TableBlock.tsx`, porting from `types/table/TablePanel.tsx` and adopting the `rows[].cells` shape from data-model.md
- [X] T024 [P] [US1] Implement the chart block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/ChartBlock.tsx`, porting the d3 implementation from `types/chart/ChartPanel.tsx` unchanged
- [X] T025 [P] [US1] Implement the metric block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/MetricBlock.tsx`
- [X] T026 [P] [US1] Implement the image block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/ImageBlock.tsx`, resolving `fileId` through the platform's existing file-access mechanism and requiring `alt`
- [X] T027 [P] [US1] Implement the divider block renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks/DividerBlock.tsx`
- [X] T028 [US1] Implement the kind→renderer map in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blockRegistry.ts` (depends on T020–T027)
- [X] T029 [US1] Implement `ContentRenderer` in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/ContentRenderer.tsx` — renders an ordered block sequence, dispatching each through the registry and falling back to a visible placeholder for an unrecognised kind (depends on T028)
- [X] T030 [US1] Dispatch content panels to `ContentRenderer` and live panels to the registry renderer in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx`'s `PanelContent`
- [X] T031 [US1] Implement `PresentPanelContentCapability` in `src/AskLucy.Application/Conversations/Capabilities/PresentPanelContentCapability.cs` — always available, declaring the committed `panel-content.schema.json` as its `InputSchemaJson` so `CapabilityExecutor` validates composed content before anything is pushed (research D1)
- [X] T032 [US1] Register `PresentPanelContentCapability` in `src/AskLucy.Application/DependencyInjection.cs` alongside the existing capabilities
- [X] T032a [US1] Add `PresentPanelContentCapability` to the shared theory in `tests/AskLucy.Application.Tests/Conversations/Capabilities/CapabilityContractTests.cs` (`Build()`, around L44–L52). That theory is written so a capability not listed there evades every contract assertion — adding it is what makes it tested
- [X] T032b [US1] Update both system agent definitions that grant the retired capability in `src/AskLucy.Application/Conversations/SystemAgents/SystemAgentDefinitions.cs`: `lucy.knowledge` (L92) and `lucy.viewer` (L131) list `OpenVisualPanelCapability.CapabilityKey`, and their `ToolUsageRules` prose names `open_visual_panel` (L90, L129). Switch both to `PresentPanelContentCapability.CapabilityKey` and rewrite the prose accordingly. **Without this, T060 is a compile break and both sub-agents silently lose the ability to show anything** — the agent-facing description text is part of the contract, not commentary
- [X] T033 [US1] Delete the four panel type modules under `src/AskLucy.Web/ClientApp/src/viewer/panels/types/` (`chart/`, `table/`, `parameters/`, `summary/`) and empty the side-effect barrel `types/index.ts`, removing its import from `features/viewer/components/ViewerSurface.tsx` (research D8)

**Checkpoint**: Lucy can present any composition from the vocabulary; the four retired types are reproducible as blocks; nothing registers a panel kind at import time any more

---

## Phase 4: User Story 2 — User Acts on Panel Content to Drive the Viewer (Priority: P2)

**Goal**: Blocks and block entries carry actions that drive the viewer, from a closed allowlist that model output cannot escape.

**Independent Test**: Present content whose entries carry actions, activate them, and confirm the viewer performs exactly the expected action — and that disallowed or malformed actions render inert and never execute.

### Tests for User Story 2

- [X] T034 [P] [US2] Unit tests for the allowlist in `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/allowlist.test.ts` — every listed command validates its arguments; a command not on the list is rejected; `removeLayer`, `addLayer`, `displayContent` and `createOverlay` are confirmed absent and unreachable (contracts/action-allowlist.md)
- [X] T035 [P] [US2] Unit tests for `useActionInvoker` in `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/useActionInvoker.test.ts` — a valid action invokes through `viewerEngine`; a command reporting failure surfaces a visible message rather than appearing to succeed (spec FR-015)
- [X] T036 [P] [US2] Unit tests for `ActionAffordance` in `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/ActionAffordance.test.tsx` — an entry with a valid action is activatable and keyboard reachable; an entry with no action is neither
- [X] T037 [P] [US2] Accessibility test in `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/ActionAffordance.a11y.test.tsx` confirming actionable entries are operable by keyboard alone with visible focus (spec SC-008)

### Implementation for User Story 2

- [X] T038 [US2] Implement the closed allowlist in `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/allowlist.ts` — an explicit map from each permitted command to an argument schema and a written-out invoker, with no dynamic dispatch by name (research D6, contracts/action-allowlist.md). **Narrowed from 7 to 6 commands during implementation**: `fitBounds` was dropped — it exists on the concrete `ViewerEngine` but was never published on `IViewerEngine`, and publishing it would itself be a viewer-command change, which this feature's Constraints keep out of scope (research D6 update)
- [X] T039 [US2] Add the optional `action` field to the keyValue, table row and metric schemas in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks.ts`, then regenerate and recommit `panel-content.schema.json` (T007's artifact)
- [X] T040 [US2] Implement `useActionInvoker` in `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/useActionInvoker.ts` — validates against the allowlist, invokes via `viewerEngine`, and surfaces a command result failure to the user
- [X] T041 [US2] Implement `ActionAffordance` in `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/ActionAffordance.tsx` — shared activatable presentation, validating at render time so a rejected action is never presented as activatable (research D6)
- [X] T042 [P] [US2] Wire `ActionAffordance` into the keyValue block renderer in `content/blocks/KeyValueBlock.tsx`
- [X] T043 [P] [US2] Wire `ActionAffordance` into the table block renderer's rows in `content/blocks/TableBlock.tsx`
- [X] T044 [P] [US2] Wire `ActionAffordance` into the metric block renderer in `content/blocks/MetricBlock.tsx`

**Checkpoint**: Content drives the viewer; the allowlist is the only route, and it is closed

---

## Phase 5: User Story 3 — Panels Are Framed to Suit Their Content (Priority: P3)

**Goal**: A panel declares its chrome — title bar or not, resizable or not, default size — and stays fully controllable either way.

**Independent Test**: Open panels declaring different framing and confirm each renders as declared while remaining movable, minimisable, closable and focusable.

### Tests for User Story 3

- [X] T045 [P] [US3] Unit tests for chrome resolution in `src/AskLucy.Web/ClientApp/src/viewer/panels/chrome/chrome.test.ts` — request overrides apply, defaults fill gaps, the minimum size floor is respected
- [X] T046 [P] [US3] Update `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.test.tsx` for the chrome variants, keeping every existing drag, resize, minimise, restore, close and focus assertion
- [X] T047 [P] [US3] Accessibility test in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.a11y.test.tsx` covering a panel with no title bar — the grip is focusable, labelled, and supports arrow-key movement (spec FR-019, SC-008)

### Implementation for User Story 3

- [X] T048 [US3] Resolve chrome for content and live panels in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.ts`, replacing the standalone `resizable` field with the resolved chrome on the panel
- [X] T049 [US3] Render the title-bar and no-title-bar variants in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx`, implementing the grip affordance — focusable, labelled, carrying the existing drag handle class and the existing arrow-key nudge handler, with close and minimise beside it (research D7)
- [X] T050 [US3] Drive `enableResizing` from the resolved chrome rather than the retired panel field in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx`

**Checkpoint**: All three framing variants work and are fully keyboard operable

---

## Phase 6: User Story 4 — Unrecognised or Unsafe Content Degrades Visibly (Priority: P3)

**Goal**: Every unrenderable block, rejected action and unregistered live kind produces a visible outcome, and nothing valid around it is lost.

**Independent Test**: Submit an unknown block kind, a malformed block, a disallowed action and an unregistered live panel kind, and confirm each produces a visible, understandable outcome while everything valid still renders.

### Tests for User Story 4

- [X] T051 [P] [US4] Unit tests for partial rendering in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/ContentRenderer.test.tsx` — a document mixing valid, unknown-kind and malformed blocks renders every valid block and a distinct visible state for each bad one (spec SC-007)
- [X] T052 [P] [US4] Unit tests confirming a rejected action is rendered inert and its refusal recorded, in `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/allowlist.test.ts`
- [X] T053 [P] [US4] Backend test confirming content with no blocks is refused before any push, in `tests/AskLucy.Application.Tests/`
- [X] T054 [P] [US4] Backend tests for `OpenLivePanelCapability` in `tests/AskLucy.Application.Tests/` — unavailable when no live kind is registered, and its request shape validated

### Implementation for User Story 4

- [X] T055 [US4] Add per-block malformed and unknown-kind visible states to `src/AskLucy.Web/ClientApp/src/viewer/panels/content/ContentRenderer.tsx`, isolating each failure to its own block
- [X] T056 [US4] Record every rejected action for diagnosis in `src/AskLucy.Web/ClientApp/src/viewer/panels/actions/allowlist.ts`, so a model repeatedly attempting a disallowed command is visible to the team (contracts/action-allowlist.md)
- [X] T057 [US4] Enforce the non-empty document rule in `PresentPanelContentCapability` so a composition with no renderable blocks opens no panel and reports why (research D11, spec FR-030)
- [X] T058 [US4] Implement `OpenLivePanelCapability` in `src/AskLucy.Application/Conversations/Capabilities/OpenLivePanelCapability.cs` (contracts/panel-request.md). **Its availability is honestly `false` in this feature**: the registry is client-side, no live kind ships here, and the server has no mechanism to learn what is registered — the same boundary that forced the retired capability to hardcode a type list. Do not reintroduce a server-side list to work around it. `IsAvailable` returns false with a comment pointing at specs/050, which supplies both the first live kinds and the reporting that makes this answerable
- [X] T058a [US4] Add `OpenLivePanelCapability` to the shared theory in `tests/AskLucy.Application.Tests/Conversations/Capabilities/CapabilityContractTests.cs` (`Build()`)
- [X] T059 [US4] Keep the existing unknown-type fallback panel working for an unregistered live kind in `src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx`, now reached only by live panels (spec FR-025)

**Checkpoint**: Every failure path in the spec's User Story 4 produces a visible outcome

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T060 Delete `src/AskLucy.Application/Conversations/Capabilities/OpenVisualPanelCapability.cs` including its hardcoded `RenderableTypeKeys` set, and remove its registration from `src/AskLucy.Application/DependencyInjection.cs` (spec FR-023, SC-002). **Requires T032b first** — two system agent definitions still reference the capability key. Commit this together with T060a; between them the backend does not compile
- [X] T060a Remove the retired capability from its two test sites in the same commit as T060: the `Build()` entry in `tests/AskLucy.Application.Tests/Conversations/Capabilities/CapabilityContractTests.cs` (L51) and the `OpenVisualPanel_ShouldBecomeUnavailable_AtTheConcurrentPanelCap` test in `CapabilityAvailabilityTests.cs` (L170–L180), re-pointing the capacity assertion at whichever new capability now carries the concurrent-panel gate
- [X] T061 Narrow `src/AskLucy.Web/ClientApp/src/viewer/panels/registry.ts` to live panel kinds only, extending its definition with declared chrome and supporting withdrawal of a registered kind (spec FR-022, preparing for specs/050)
- [X] T062 Update `src/AskLucy.Web/ClientApp/src/viewer/panels/registry.test.ts` for the narrowed registry and the withdrawal path
- [X] T063 Update the devtools request fixtures in `tests/AskLucy.E2E.Tests/AiFloatingPanels.spec.ts` to the new discriminated request shape, leaving its assertions unchanged (research D9)
- [X] T064 [P] Update `src/AskLucy.Web/ClientApp/src/viewer/README.md` to describe the content vocabulary, the action allowlist and the narrowed registry
- [X] T065 Verify no capability, component or test still references `chart`, `table`, `parameters` or `summary` as a panel type key anywhere in the repository
- [X] T066 Run `npx tsc -b --noEmit` from `src/AskLucy.Web/ClientApp` — note the `-b` flag; a bare `tsc --noEmit` checks nothing in this repo
- [X] T067 Run `npm run lint` and `npm test` (the **full** frontend suite, not only touched files — `ChatPage.test.tsx` asserts panel behaviour independently of the panel components' own tests)
- [X] T068 Run `dotnet test` and confirm the backend suites pass
- [X] T069 Walk every scenario in `specs/049-panel-content-model/quickstart.md`, including Scenario 10's manual regression pass over drag, resize, minimise/restore, close, focus and stacking, opacity, cascade placement and eviction. **Verified via the automated suite, not a live browser** (no browser access in this environment) — every scenario has a direct automated equivalent that was written and run as part of the phases above: Scenario 1/2 → block + ContentRenderer tests; Scenario 3/4 → ActionAffordance/useActionInvoker/allowlist tests; Scenario 5 → ContentRenderer's mixed-document test; Scenario 6 → chrome.test.ts + FloatingPanel chrome describe block; Scenario 7/8 → PresentPanelContentCapabilityTests.cs; Scenario 9 → blocks.schema.test.ts; Scenario 10 → the full 940-test frontend suite plus every original FloatingPanel behaviour test kept intact. **Still needs genuine human/browser QA before this ships**: real pointer drag/resize interaction, real keyboard Tab traversal across a live page, and the light/dark theme toggle — these are exactly what react-rnd's jsdom mock and jest-axe cannot substitute for

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational. No dependency on other stories
- **US2 (Phase 4)**: Depends on Foundational and on US1's block renderers existing (T042–T044 wire into them)
- **US3 (Phase 5)**: Depends on Foundational only — genuinely independent of US1 and US2
- **US4 (Phase 6)**: Depends on US1 (ContentRenderer) and US2 (allowlist) for the paths it hardens
- **Polish (Phase 7)**: Depends on all desired stories

### Within Each User Story

- Tests before implementation
- Block renderers before the registry before `ContentRenderer`
- Allowlist before the invoker before the affordance before wiring into renderers

### Parallel Opportunities

- T001–T003 in parallel
- T014–T019 (all US1 tests) in parallel
- T020–T027 (all eight block renderers) in parallel — different files, no interdependencies
- T034–T037 (all US2 tests) in parallel
- T042–T044 (wiring the affordance into three renderers) in parallel
- T045–T047 (all US3 tests) in parallel
- T051–T054 (all US4 tests) in parallel
- **US3 can run fully in parallel with US1 and US2** — chrome touches `FloatingPanel.tsx` and the store's chrome resolution, neither of which the content or action work depends on. Note T049 and T030 both edit `FloatingPanel.tsx`, so sequence those two.

---

## Parallel Example: User Story 1

```bash
# All eight block renderers together — different files, no interdependencies:
Task: "Implement the heading block renderer in content/blocks/HeadingBlock.tsx"
Task: "Implement the text block renderer in content/blocks/TextBlock.tsx"
Task: "Implement the keyValue block renderer in content/blocks/KeyValueBlock.tsx"
Task: "Implement the table block renderer in content/blocks/TableBlock.tsx"
Task: "Implement the chart block renderer in content/blocks/ChartBlock.tsx"
Task: "Implement the metric block renderer in content/blocks/MetricBlock.tsx"
Task: "Implement the image block renderer in content/blocks/ImageBlock.tsx"
Task: "Implement the divider block renderer in content/blocks/DividerBlock.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup
2. Phase 2: Foundational — **critical**, blocks everything
3. Phase 3: User Story 1
4. **STOP and VALIDATE**: quickstart Scenarios 1 and 2 — compose a location panel with no new code, and reproduce all four retired presentations
5. At this point the feature's headline claim is already provable

### Incremental Delivery

1. Setup + Foundational → vocabulary and request shape settled
2. US1 → content renders, four types retired (**MVP** — spec SC-001 and SC-003 demonstrable)
3. US2 → content drives the viewer (spec SC-005 demonstrable)
4. US3 → panels framed to suit their content
5. US4 → every failure path visible (spec SC-006 and SC-007 demonstrable)
6. Polish → the hardcoded server-side list is gone (spec SC-002), full regression pass

### Risk Notes

- **T004 is a gate, not a formality.** The plan's only tracked complexity is mitigated entirely by schema generation. If T004 finds that generation is unusable, T007/T008 change shape before any schema is written — discovering it at T007 means rewriting the vocabulary.
- **T032b must precede T060.** Two system agent definitions reference the retired capability key, and their prose names the retired tool. Deleting the capability first is a compile break; deleting it without the prose rewrite leaves two sub-agents instructed to use a tool that no longer exists.
- **T060 and T060a belong in one commit.** Between them the backend does not compile.
- **T033 is the irreversible frontend step.** Deleting the four type modules breaks any caller still using the old keys. Do it only once US1's renderers and the capability are working, and pair it with T065's repository-wide check.
- **T007/T039 both regenerate the committed schema artifact.** T039 changes the vocabulary after T007 established it; forgetting to regenerate leaves the server rejecting valid actions. T008's parity test catches this, which is why it exists before either.
- **T030 and T049 both edit `FloatingPanel.tsx`.** Sequence them rather than running US1 and US3 fully concurrently on that file.
- **T013a touches a shared record.** `TurnContext` is constructed in the factory and asserted across several capability test files; expect the compiler to find the call sites, but run the full backend suite rather than the touched project.

---

## Notes

- [P] = different files, no dependencies
- Commit after each task or logical group
- Run the full frontend suite, never just the touched files
- No compatibility shim for the four retired type keys — the break is intentional (spec Assumptions)
