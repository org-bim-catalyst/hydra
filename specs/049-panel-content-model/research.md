# Phase 0 Research: Panel Content Model

**Feature**: `049-panel-content-model` | **Date**: 2026-09-12

All Technical Context unknowns are resolved below. Each decision records what was chosen, why, and what was rejected.

---

## D1 — Where server-side validation happens

**Decision**: Declare the block vocabulary as the capability's `InputSchemaJson`. No new validation layer.

**Rationale**: `CapabilityExecutor.ExecuteAsync` already parses `capability.InputSchemaJson` and validates the model's arguments against it through `IJsonSchemaValidator`, *before* the capability runs and against a schema the deciding model was never shown ([CapabilityExecutor.cs:95-117](../../src/AskLucy.Application/Conversations/Runtime/CapabilityExecutor.cs#L95-L117)). That is precisely the independent verification this feature needs, and it is already wired, already logged on refusal, and already covered by a 32 KB argument ceiling. `SuggestedActionGrounder` applies the same schema check to offered rows.

**Alternatives rejected**:
- *A dedicated panel-content validator in Application* — duplicates an enforcement point that already exists, and would need its own refusal, logging and size-limit handling.
- *Validating in the hub or the notifier* — too late; the capability would already have reported success.

**Consequence**: The vocabulary must be expressible as JSON Schema, which constrains D2's shape choice (no constructs the validator cannot express).

---

## D2 — Keeping the two vocabulary definitions in step

**Decision**: zod is the single source. `contracts/panel-content.schema.json` is generated from it with zod 4's `z.toJSONSchema()`, committed, and declared by the C# capability. A test regenerates and compares, failing on any difference.

**Rationale**: Both sides genuinely need to validate — the server to keep the grounding guarantee, the client because it is the only place that can render seven good blocks and one visible error rather than rejecting the whole document. Neither runtime can consume the other's native format. Generation with a drift test converts a silent divergence into a failing build, which is the outcome the constitution's DRY rule is actually protecting. zod 4.1 is already a dependency and `z.toJSONSchema()` is available in it.

**Alternatives rejected**:
- *Hand-maintain both* — the exact drift this feature exists to remove; `OpenVisualPanelCapability` carries a source comment admitting it.
- *Define in C#, generate TypeScript* — the client needs runtime validation, not just types, so it would still need a hand-written zod schema.
- *Server validates loosely, client strictly* — surrenders the grounding property for the one payload most directly composed by the model.

**Resolved**: generation is a **test-only** assertion, not a build step — the artifact stays reviewable in diffs and nothing couples to the build. Implemented by T007 (generate and commit) and T008 (regenerate and compare).

**Fallback if generation proves unusable** (verified by T004, which gates Phase 2): if `z.toJSONSchema()` is non-deterministic or cannot express the vocabulary's constructs, the JSON Schema becomes hand-written and authoritative, and T008 changes from a generation-parity test to a **shared-fixture parity test** — a committed corpus of valid and invalid documents that the zod schema and the JSON Schema must classify identically. That still converts drift into a failing build, which is the property the mitigation exists for; it costs a maintained corpus instead of a generator.

**T004 outcome (2026-09-12)**: the happy path holds. Verified against zod 4.4.3 (satisfies the `^4.1.13` dependency range) with a representative discriminated union covering every construct the vocabulary needs — `discriminatedUnion` on `kind`, nested object arrays, optional fields, `min`/`max` on both strings and arrays, and a `z.literal` version field. `z.toJSONSchema()` produced a `$schema: https://json-schema.org/draft/2020-12/schema` document — the same draft `Json.Schema.Net` (the library backing `IJsonSchemaValidator`, see D1) targets — with `oneOf` for the union, `minLength`/`maxLength`, `minItems`/`maxItems`, and `additionalProperties: false` all present and correctly nested. Two independent calls on the same schema produced byte-identical output. No fallback needed; T007/T008 proceed as originally planned.

---

## D3 — Content document shape

**Decision**: A flat, ordered array of blocks, each discriminated by a `kind` field. No nesting.

**Rationale**: Matches the spec's "ordered sequence of blocks" exactly. Flat arrays are trivially expressible in JSON Schema (D1's constraint), trivially validated, and are the shape a language model composes most reliably. Rendering is a map over the array.

**Alternatives rejected**:
- *A nested tree with container blocks* — no requirement calls for nesting; it introduces recursion into both validation and rendering, and recursive JSON Schema is where validators diverge.
- *Free-form markdown* — forbidden by §8 (the content is untrusted model output) and unable to carry actions.

---

## D4 — Block kinds in v1

**Decision**: `heading`, `text`, `keyValue`, `table`, `chart`, `metric`, `image`, `divider`. Vocabulary version `1`.

**Rationale**: Exactly the set the spec names (FR-001), no more. Four map directly onto existing renderers, so most of the work is relocation rather than new code: `table` and `chart` reuse their current implementations (the chart renderer is hand-built on d3, so there is no chart library to reconsider), `keyValue` absorbs `parameters`, and `heading`/`text` absorb `summary`.

**Alternatives rejected**: Adding `list`, `code`, `badge`, `timeline` and similar in anticipation — YAGNI (§2.III). The vocabulary is versioned precisely so it can grow deliberately.

---

## D5 — Panel request shape

**Decision**: `PanelRequest` becomes a discriminated union on a `kind` field: `{ kind: 'content', title, content, chrome?, ... }` or `{ kind: 'live', title, typeKey, data, chrome?, ... }`. Shared fields (`requestId`, `position`, `contextAssociation`) stay common.

**Rationale**: The two cases differ in what they carry and how they are validated, so an explicit discriminator is honest. It also makes the store's branch obvious rather than inferred from whether a reserved type key was used.

**Alternatives rejected**:
- *Reserve `typeKey: "content"`* — overloads a field whose meaning is "which registered renderer", which is exactly the conflation this feature removes.
- *Two separate hub events* — doubles the transport surface for one payload that is otherwise identical in delivery, ordering and per-user routing.

---

## D6 — Action representation and enforcement

**Decision**: An action is `{ command, args }`. The client holds a closed map from command name to a pair of (argument schema, invoker bound to `viewerEngine`). Validation happens at render time, not activation time: an action that fails validation is rendered inert. There is no dynamic dispatch — no indexing `viewerEngine` by a model-supplied string.

**Rationale**: §8 treats model output as untrusted. A closed map with explicit invokers means the set of reachable operations is fixed at build time and visible by reading one file. Validating at render time rather than on click satisfies the spec's requirement that a rejected action never *appears* activatable (FR-013, FR-014), which is stronger than refusing on activation.

**Initial allowlist** (spec Assumptions): `select`, `clearSelection`, `zoomToLocation`, `setLayerVisibility`, `setViewMode`, `setMapStyle`. Every one is idempotent and only changes what is shown or selected. Deliberately excluded: `addLayer`, `removeLayer`, `displayContent`, `createOverlay` — these mutate viewer content and belong to specs/051's content commands, where they can be designed with the right guarantees. Also excluded: `fitBounds`, which exists on the concrete `ViewerEngine` (specs/038) but was never published on `IViewerEngine` — discovered during implementation (T038). Widening that published interface is itself a viewer-command change, which this feature's Constraints keep out of scope; `zoomToLocation` covers framing for v1.

**Alternatives rejected**:
- *Allow any `IViewerEngine` method by name* — a prompt-injection foothold, and it would let content mutate viewer state.
- *Server-side action validation only* — the client is the only place invocation can actually be prevented, and panel requests can originate client-side.
- *Arbitrary callback URLs or script* — out of the question under §8.

---

## D7 — Chrome declaration and the no-title-bar affordance

**Decision**: Chrome is `{ titleBar: boolean, resizable: boolean, defaultSize: {width, height} }`. Content panels get a default (title bar, resizable, 400×300) that a request may override within the existing minimum-size floor. Live panel kinds declare chrome at registration, extending today's `defaultSize`/`resizable` fields.

For a panel with no title bar: a small, visually distinct **grip affordance** in a corner, focusable, labelled, carrying the drag handle class and the existing arrow-key nudge handler, with the close and minimise controls beside it.

**Rationale**: `FloatingPanel.tsx` already attaches drag to a handle class and already implements keyboard nudging on that handle ([FloatingPanel.tsx:23-39, 210-238](../../src/AskLucy.Web/ClientApp/src/viewer/panels/components/FloatingPanel.tsx#L23-L39)). A grip reuses both unchanged. It is also unambiguous in a way that body-dragging is not — a small circular readout is mostly content, and dragging by content fights any interactive element inside it.

**Alternatives rejected**:
- *Drag by the panel body when no interactive element is under the pointer* — unpredictable, and directly conflicts with the actionable content this feature introduces.
- *Always render a title bar and let panels hide it visually* — leaves an invisible focusable element and a hit target users cannot see.
- *No-title-bar panels are fixed in place* — fails FR-019.

---

## D8 — Migrating the four existing renderers

**Decision**: Move the implementations, keep their zod schemas as block schemas, delete the type registrations and the side-effect import barrel.

| Retired type | Becomes | Reuse |
|---|---|---|
| `table` | `table` block | Renderer and `tableDataSchema` move essentially unchanged; rows gain an optional action |
| `chart` | `chart` block | d3 renderer and `chartDataSchema` move unchanged |
| `parameters` | `keyValue` block | Renderer and schema move; naming aligns with the vocabulary |
| `summary` | `heading` + `text` blocks | Prose splits into the two structural blocks |

`types/index.ts` — the side-effect barrel that registers all four — is emptied, and the registry's population becomes dynamic (extensions register live kinds in specs/050; nothing registers at import time here).

**Rationale**: The four renderers are already the right code; only their addressing changes. This keeps the migration low-risk and makes the spec's SC-003 ("every previous presentation reproducible") largely a matter of re-pointing tests.

---

## D9 — Preserving specs/028 behaviour

**Decision**: The store keeps its existing shape. `openPanel` gains a branch on request kind; everything after panel construction — cascade placement, z-order, LRU eviction, minimise/restore, clamping, context association, the two `viewerEngine` subscriptions — is untouched.

**Rationale**: Every behaviour in spec FR-027 lives in code paths after construction ([floatingPanelStore.ts:80-185](../../src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.ts#L80-L185)). Confining the change to resolution and validation is what makes "existing coverage passes in substance" achievable rather than aspirational.

**Known test impact**: `AiFloatingPanels.spec.ts` drives panels through `window.__askLucyFloatingPanelStore.openPanel` with the old request shape, so its fixtures change even though its assertions do not. `ChatPage.test.tsx` has its own panel assertions independent of the panel components' own tests — the full suite must run, not only the touched files.

---

## D10 — Image block source

**Decision**: An image block carries `fileId` — a platform file identifier, not a URL. The renderer never interpolates it into a raw `<img src>`; it is always passed as an opaque parameter to one fixed, trusted endpoint (the platform's existing file-download mechanism), which resolves it to a signed URL only if the requesting user is entitled to that file. The schema itself does not attempt to pattern-match "is this a URL" — that protection is architectural, not a regex.

**Rationale**: An unconstrained URL in model-composed content is both an exfiltration channel (a request to an attacker-controlled host reveals that the user viewed the content, plus their address and agent) and a request-forgery vector from the browser. A schema-level rejection of URL-*shaped* strings is exactly the kind of pattern an attacker can bypass (encoding, alternate schemes, a value that only looks like an id). The stronger guarantee is architectural: `fileId` is never treated as anything other than an opaque identifier handed to the platform's own entitlement-checked endpoint, so it is structurally impossible for content to make the browser fetch an arbitrary address, regardless of what string is supplied. A value that isn't a real file id simply fails to resolve — the image shows as unavailable (spec Edge Cases), not as a request to wherever the string pointed.

**Alternatives rejected**:
- *Reject URL-shaped strings in the schema* — the initial framing (superseded here after implementation revealed the actual mechanism was architectural, not a validation rule) — gives a false sense of the protection's strength and is bypassable.
- *Allow any HTTPS address with a client-side allowlist of hosts* — a host allowlist is a maintenance burden with no owner, and it still permits the timing signal.

---

## D11 — Empty and unrenderable content

**Decision**: Content with no blocks, or whose every block is unrenderable, opens no panel; the outcome is reported through the capability's normal failure path so Lucy can say what happened. A document with a *mix* of valid and invalid blocks opens, rendering the valid ones and a visible placeholder per invalid one.

**Rationale**: Spec FR-030 forbids opening an empty panel, and User Story 4 requires partial rendering. The distinction is where the whole-document check sits: at the capability, before a request is pushed — not in the renderer, which would have already opened a panel by the time it discovered there was nothing to draw.
