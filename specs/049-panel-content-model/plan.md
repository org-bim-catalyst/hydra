# Implementation Plan: Panel Content Model

**Branch**: `049-panel-content-model` | **Date**: 2026-09-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/049-panel-content-model/spec.md`

## Summary

Replace the four registered panel types (`chart`, `table`, `parameters`, `summary`) with a versioned vocabulary of content blocks that Lucy composes freely, rendered by one presentation. Blocks and block entries may carry declarative actions drawn from a closed allowlist over the viewer's existing command surface. Panels declare their own chrome. Panel-kind registration narrows to "live" panels only, and the server's hardcoded type list is removed.

The technical approach rests on one discovery: `CapabilityExecutor` already validates a capability's arguments against its declared `InputSchemaJson` using `IJsonSchemaValidator`, before execution and against a schema the deciding model never sees. Declaring the block vocabulary there *is* the server-side enforcement — no new validation layer is needed, and the "grounding" property the platform already relies on extends to panel content for free.

## Technical Context

**Language/Version**: TypeScript ~6.0 (frontend, `strict`), C# / .NET 10 (backend)

**Primary Dependencies**: React 19.2, MUI 9.2, Zustand 5.0, zod 4.1, react-rnd 10.5, d3 7.9 (existing chart renderer); MediatR-based Application layer, existing `IJsonSchemaValidator`

**Storage**: None new. Panel state is session-scoped in a Zustand store with no persistence; the opacity preference uses the existing `panelPreferencesStore`.

**Testing**: Vitest (`npm test` → `vitest run`) for unit and a11y; Playwright for E2E (`tests/AskLucy.E2E.Tests/AiFloatingPanels.spec.ts`); xUnit for backend

**Target Platform**: Browser (the existing workspace viewer surface), backed by the ASP.NET Core API

**Project Type**: Web application — frontend-dominant feature with a small, well-bounded backend change

**Performance Goals**: A panel renders without perceptible delay at the vocabulary's practical limits. Table and chart blocks must stay usable at large sizes without blocking the viewer; the viewer's own interactivity must not regress.

**Constraints**: Model output is untrusted — no block content may be interpreted as markup, and no action may reach a command outside the allowlist. Existing specs/028 behaviour must be preserved exactly. No compatibility shim for the four retired type keys.

**Scale/Scope**: 8 block kinds, ~7 allowlisted actions, 4 existing renderers migrated to blocks, 1 backend capability split into 2, ~10 frontend modules touched.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture / Dependency Rule** | PASS. The vocabulary's server-side definition lives in Application as a capability's declared schema. No Domain involvement; no Infrastructure reference from Application. The frontend reaches the backend only over the existing hub contract. |
| **II. SOLID** | PASS. One block kind = one renderer with a single reason to change. New block kinds extend the vocabulary rather than editing a closed switch (OCP), via a kind→renderer map. |
| **III. Simplicity — DRY, KISS, YAGNI** | PARTIAL — see Complexity Tracking. The block vocabulary is expressed twice, as zod on the client and JSON Schema on the server. Mitigated by generation plus a drift test rather than accepted as duplication. |
| **IV. Composition over Inheritance** | PASS. Blocks compose; no inheritance introduced. |
| **V. Dependency Inversion & Testability** | PASS. Action invocation goes through the existing `IViewerEngine` facade, which is already substitutable in tests. |
| **VI. Separation of Concerns** | PASS. Block renderers present; the allowlist decides what may be invoked; the store owns panel state. No business logic in components. |
| **VII. Convention over Configuration** | PASS. Follows the established `viewer/panels/` layout, the Zustand store convention, the zod-schema convention, and the existing capability registration pattern. |
| **VIII. No Silent Failures (NON-NEGOTIABLE)** | PASS — and load-bearing. Unknown block kind → visible placeholder (FR-005 of the spec's degradation group); malformed block → visible error; rejected action → rendered inert *and* logged; empty content → no panel opened, outcome visible. Every path in the spec's User Story 4 maps to a requirement. |
| **§7 UI Principles** | PASS. MUI theme only, no hardcoded colours, light and dark both required (spec FR-007), WCAG 2.1 AA including keyboard operation of action entries and of panels with no title bar (spec FR-013, FR-019). |
| **§8 Security** | PASS — and load-bearing. Two controls: block content is never rendered as markup (no `dangerouslySetInnerHTML`), and actions are a closed, client-validated allowlist with no dynamic dispatch by name. Images resolve only through the platform's existing file access. |
| **§10 Testing** | PASS. Unit tests per block renderer and for the allowlist validator; a11y tests for the new chrome variants; the existing specs/028 suites must pass unchanged in substance. |
| **§13 Documentation** | PASS. Contracts under `contracts/`; the vocabulary is versioned and its version recorded. |

**Gate result: PASS**, with one item tracked in Complexity Tracking.

### Post-Design Re-check

Re-evaluated after Phase 1. The design does not introduce any violation the initial check did not anticipate, and it closes two risks rather than adding them:

- **§8 Security strengthened, not merely preserved.** Research D6 moved action validation from activation time to render time, so a rejected action never *appears* activatable. Research D10 pushed the image-source restriction into the schema, which means the server refuses an external address at the existing gate rather than the client declining to render it. Both are stricter than the spec required.
- **§2.VIII satisfied at two distinct layers.** Whole-document failures are caught server-side before a panel exists; single-block failures are caught client-side after one does. Research D11 makes that split explicit, so "no silent failure" holds without the contradiction of having to open an empty panel to report that it is empty.
- **§2.I unchanged.** No Domain or Infrastructure involvement emerged during design. The backend change is two capability classes and one DTO, all in Application.
- **§2.III re-examined.** The vocabulary stayed at the eight kinds the spec names; no speculative additions survived design. The one duplication remains the zod/JSON-Schema pair, still mitigated by generation plus a drift test, and Scenario 9 in quickstart.md makes that mitigation executable rather than aspirational.
- **§10 Testing.** The known trap is recorded: the full frontend suite must run, because `ChatPage.test.tsx` asserts panel behaviour independently of the panel components' own tests.

No new entries in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/049-panel-content-model/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── content-vocabulary.md
│   ├── action-allowlist.md
│   └── panel-request.md
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/viewer/panels/
├── content/                        # NEW — the vocabulary and its presentation
│   ├── blocks.ts                   # zod schemas for every block kind; the vocabulary version
│   ├── ContentRenderer.tsx         # renders an ordered block sequence
│   ├── blockRegistry.ts            # kind → renderer map (internal, not the public registry)
│   └── blocks/                     # one module per block kind
│       ├── HeadingBlock.tsx
│       ├── TextBlock.tsx
│       ├── KeyValueBlock.tsx
│       ├── TableBlock.tsx          # from types/table/TablePanel.tsx
│       ├── ChartBlock.tsx          # from types/chart/ChartPanel.tsx (d3 renderer reused)
│       ├── MetricBlock.tsx
│       ├── ImageBlock.tsx
│       └── DividerBlock.tsx
├── actions/                        # NEW — declarative actions
│   ├── allowlist.ts                # closed command set + per-command argument schemas
│   ├── useActionInvoker.ts         # validates then invokes via viewerEngine
│   └── ActionAffordance.tsx        # shared activatable presentation
├── chrome/                         # NEW — per-panel framing
│   └── chrome.ts                   # chrome declaration + defaults
├── components/
│   ├── FloatingPanel.tsx           # MODIFIED — chrome variants, no-title-bar affordance
│   └── FloatingPanelHost.tsx       # MODIFIED — content vs live dispatch
├── store/
│   └── floatingPanelStore.ts       # MODIFIED — content|live request handling
├── types/
│   ├── panel.ts                    # MODIFIED — PanelRequest becomes discriminated
│   └── index.ts                    # EMPTIED — the four registrations are removed
└── registry.ts                     # MODIFIED — narrowed to live panel kinds

src/AskLucy.Application/
├── Conversations/Capabilities/
│   ├── PresentPanelContentCapability.cs      # NEW — always available; declares the vocabulary
│   └── OpenLivePanelCapability.cs            # REPLACES OpenVisualPanelCapability
└── Panels/
    └── PanelRequestDto.cs                     # MODIFIED — discriminated content|live

src/AskLucy.Web/ClientApp/src/viewer/panels/**/*.test.ts(x)   # unit + a11y
tests/AskLucy.E2E.Tests/AiFloatingPanels.spec.ts               # MODIFIED — devtools shape
```

**Structure Decision**: The feature is frontend-dominant and stays inside the existing `viewer/panels/` tree, adding three sibling directories (`content/`, `actions/`, `chrome/`) alongside the existing `components/`, `store/`, `types/`. This follows §4's frontend convention (feature-domain folders) and keeps the change reviewable as one coherent area. The backend change is confined to two capability classes and one DTO in Application — no Domain, Infrastructure or persistence change.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| The block vocabulary is expressed twice — zod on the client, JSON Schema on the server | The server must reject invalid content before it is ever pushed (this is the grounding property `CapabilityExecutor` already provides), and the client must validate independently because it is the only place that can degrade a single bad block while rendering the rest. Neither side can consume the other's native format at runtime. | **Server-only validation** was rejected because a panel request can also originate client-side (devtools, tests, future extensions), leaving the renderer unprotected. **Client-only validation** was rejected because it would let unvalidated model output reach the wire and lose the existing grounding guarantee. **Hand-maintaining both** was rejected as exactly the drift this feature exists to remove. The accepted mitigation is generation: the zod schema is the single source, `contracts/panel-content.schema.json` is generated from it via zod 4's `z.toJSONSchema()` and committed, the C# capability declares that committed artifact, and a test fails if the committed file no longer matches what zod generates. Drift becomes a failing build rather than a silent divergence. **Task T004 gates this** — it proves the generator is deterministic and expressive enough before any schema is written. If it is not, the fallback (research D2) makes the JSON Schema hand-written and authoritative, with the parity test running a shared fixture corpus through both sides instead. Either path keeps drift a build failure; only the cost differs. |
