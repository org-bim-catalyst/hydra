# Implementation Plan: Solar Analysis Accuracy & Performance

**Branch**: `063-solar-accuracy-performance` | **Date**: 2026-09-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/063-solar-accuracy-performance/spec.md`

## Summary

One correctness repair and four efficiency repairs inside the existing solar analysis capability
(specs/052). No new user-facing feature, no backend change, no new dependency, no change to the
viewer extension framework.

The correctness work separates two physical effects that the `−0.833°` rise/set threshold bundles
together — atmospheric refraction and the sun's angular radius — and applies each where it belongs.
Refraction is applied to the reported altitude and becomes the system's single altitude, used for
the figure, the light direction and the sun marker alike. The sun's radius defines what "risen"
means, and rise and set are solved for by iteration rather than approximated by a threshold applied
to a once-per-day position. The result is a reported altitude at sunrise of exactly **−0.267°** at
every site and date, constant by construction (research D1–D4).

The efficiency work merges 300 footprint meshes into one, derives the shadow radius from the
geometry present instead of from an undelivered constant, gates shadow recomputation during
playback on sun movement rather than on frames, and stops rebuilding the fixed dome furniture every
time the date changes (research D5–D9).

## Technical Context

**Language/Version**: TypeScript 5.x (`strict`), React 19, targeting the browser

**Primary Dependencies**: Three.js (existing; `BufferGeometryUtils.mergeGeometries` from its own
`examples/jsm`, already transitively present), Zustand, MUI. **No new package is added.**

**Storage**: N/A — no persisted state changes. Session-scoped `correctionsStore` is untouched.

**Testing**: Vitest + Testing Library (`npx vitest run src/features/solar`). The solar math modules
are pure functions with no DOM or GPU involvement and are unit-tested directly (constitution §2 V).

**Target Platform**: Browser, WebGL2 via the viewer extension framework (specs/049–051)

**Project Type**: Frontend-only change within an existing feature-domain folder

**Performance Goals**: Continuous, stutter-free scrubbing and playback at the existing 300-building
cap and 200 m default radius (SC-004)

**Constraints**: Renderer-global and scene-global state remain framework-owned and MUST NOT be
touched (FR-024, specs/052 FR-038, README constraint 2). The single-radius invariant behind
README constraint 4 MUST be preserved literally (FR-009). Drawing resources MUST NOT leak across
date, site, or open/close cycles (FR-023).

**Scale/Scope**: 8 existing files modified, 1 added; ~5 existing test files extended, 2 added.
No backend, no API, no migration.

## Constitution Check

*GATE: passed before Phase 0, re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture** | N/A for this change — entirely within `ClientApp/src/features/solar`, which does not cross layer boundaries. No backend code is touched. |
| **II. SOLID** | Improved. `sunPathCurve.ts`'s `buildSunPath` currently has two reasons to change (site/year furniture, and the chosen date); D9 splits them (SRP). `RemoteFileDownloader`-style constructor injection of the varying part is mirrored by passing the derived radius rather than reading a module constant. |
| **III. Simplicity / YAGNI** | Directional shadow-region extension is explicitly deferred (FR-014a) rather than built speculatively. No abstraction is introduced for a hypothetical second refraction model. |
| **IV. Composition** | Preserved — the split dome is two sibling groups, not a class hierarchy. |
| **V. Dependency Inversion & Testability** | Preserved and extended. `solarPosition.ts`, `daySummary.ts` and the new refraction module remain pure functions of their arguments, unit-testable with no GPU or DOM. |
| **VI. Separation of Concerns** | Preserved. Panels continue to render figures computed elsewhere; no calculation moves into a component. |
| **VII. Convention Over Configuration** | Follows the existing module layout and the established practice of exporting tolerance constants as the single source for both assertions and user-facing wording. |
| **VIII. No Silent Failures (NON-NEGOTIABLE)** | Two new surfaces require attention and are covered by FR-028 and by tasks: the root-finder must not silently return a wrong time if it fails to converge, and the new below-1° no-shadow window must be *stated* to the user, not left looking like a failure (research D7). |
| **§4 TypeScript** | `strict` already on; no `any` introduced. New exported functions carry TSDoc summaries of intent. |
| **§4 Magic values** | Every new number — the semidiameter, the 1° shadow floor, the 0.25° gate — is a named exported constant, consistent with the existing tolerance constants. |
| **§4 Folder structure** | Unchanged; all work lands under `src/features/solar/` per feature-domain convention. |
| **§5 Database** | N/A — no persistence. |
| **§6 API** | N/A — no endpoint change. The `/api/v1/site-buildings` contract from specs/052 is untouched. |

**Gate result: PASS.** No violations to justify; the Complexity Tracking table is omitted.

One item is flagged rather than violated: research D8 records that the conventional Three.js
approach to shadow gating (`renderer.shadowMap.autoUpdate`) is forbidden here by FR-024, and states
the sanctioned alternative — gate the inputs, and if that proves insufficient, raise a
`DrawingRequirement` against specs/051 as an SC-009 finding rather than reaching into renderer
state. This mirrors the precedent specs/052 set for its own two live framework risks.

## Project Structure

### Documentation (this feature)

```text
specs/063-solar-accuracy-performance/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — D1..D10
├── data-model.md        # Phase 1 — the quantities and their relationships
├── quickstart.md        # Phase 1 — how to validate, manually and automatically
├── checklists/
│   └── requirements.md  # Spec quality checklist (passing)
├── contracts/
│   ├── solar-position.md    # The altitude/refraction/rise-set module contract
│   └── solar-scene.md       # Geometry, shadow radius, gating and dome-split contract
└── tasks.md             # Phase 2 — NOT created by /speckit-plan
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/features/solar/
├── solar/
│   ├── refraction.ts              ADDED — NOAA piecewise correction + semidiameter constant (D2)
│   ├── solarPosition.ts           MODIFIED — returns the corrected altitude as its only altitude (D4);
│   │                              re-measured position tolerance (D10)
│   └── daySummary.ts              MODIFIED — rise/set by root-finding on the corrected upper edge (D3)
├── buildings/
│   └── footprintGeometry.ts       MODIFIED — one merged geometry, one shared material (D5)
├── scene/
│   ├── sunLight.ts                MODIFIED — content-derived radius, 1° shadow floor (D6, D7)
│   ├── shadowGround.ts            MODIFIED — sized from the same derived radius (D6)
│   ├── sunPathCurve.ts            MODIFIED — buildSunPath split into fixed + dated (D9)
│   └── SolarScene.ts              MODIFIED — owns the split groups and their disposal scopes (D9);
│                                  applies the playback gate (D8)
├── panels/
│   └── solarFiguresContent.ts     MODIFIED — states which altitude quantity is shown (FR-003)
└── copy.ts                        MODIFIED — wording for the quantity label and the
                                    below-1° no-shadow window (FR-012, constitution §7)

viewer/extensions/builtin/solarAnalysisExtension.tsx   MODIFIED — passes geometry-change signals
                                                        through the playback gate (D8, FR-019)
```

**Structure Decision**: Frontend-only, entirely inside the existing `features/solar` feature-domain
folder established by specs/052. The module map in that folder's `README.md` is part of the
deliverable and is updated in the same change, since it is the document that records the binding
constraints this work operates under.

## Phase 0 — Research

Complete. See [research.md](./research.md) for D1–D10. Both spec clarifications were resolved
before planning began; a third question — whether a refraction correction alone achieves the spec's
original target — was raised *by* the research, measured, answered "no", and returned to the user,
who chose the self-consistency option now recorded as D1/D3 and folded back into the spec.

## Phase 1 — Design & Contracts

Complete. [data-model.md](./data-model.md) defines the quantities and which of them is authoritative;
[contracts/solar-position.md](./contracts/solar-position.md) and
[contracts/solar-scene.md](./contracts/solar-scene.md) define the two module surfaces that change;
[quickstart.md](./quickstart.md) is the validation guide, written so that the manual half can be
executed by someone who is not a solar-geometry expert and judged against stated expected values.

### Post-design Constitution re-check

**PASS.** The design adds one module (`refraction.ts`), splits one function, and changes the
derivation of two numbers. It introduces no new dependency, no new layer, no new abstraction
without a present need, and no new state. The two constitution items that needed attention —
§2 VIII for the root-finder and for the no-shadow window — are carried as explicit requirements
into the contracts rather than left to implementation discretion.

## Risks

| Risk | Mitigation |
|---|---|
| The playback gate cannot be implemented without touching renderer-global shadow state | Research D8 states the sanctioned fallback: raise a `DrawingRequirement` against specs/051, do not reach into renderer state. The gate is an optimisation; the release is still worth shipping without it. |
| The re-measured position tolerance exceeds the currently advertised 0.1° | D10 makes re-measurement a task with an explicit branch: the constant and the panel wording change together, automatically, because they share one source. |
| Merging footprints loses per-building identity needed later | D5 establishes nothing picks or raycasts footprints today; `BuildingCorrectionsPanel` reads the store, not the geometry. If picking is needed by spec B, it is added then, against a stated need. |
| Splitting the dome leaks drawing resources | D9 identifies disposal as the actual hazard; FR-023, SC-010 and a dedicated task cover it, and the existing deep `disposeGroupContents` is reused rather than reimplemented. |
| Rise/set times move and look like a regression | FR-004a and SC-003 require validation against published NOAA values rather than against the current release, and the quickstart tells the user to expect the shift and where it will be largest. |
