# Implementation Plan: Particle Sphere Rendering Engine Upgrade

**Branch**: `011-particle-sphere-engine` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/011-particle-sphere-engine/spec.md`

## Summary

Replace `ReactiveSphere`'s current ring-lattice particle sphere with a uniform, golden-angle
Fibonacci-distributed particle sphere rendered with additive-blended, soft-edged point sprites
and a scoped (sphere-only) bloom pass, plus a new GPU-driven breathing animation layered on top
of the existing idle rotation and voice-reactive deformation. The "full" quality tier gets the
richer glow/bloom technique at a substantially higher particle count; the "reduced" tier
deliberately keeps a simpler, non-glowing technique (not the same technique at fewer particles);
`static-fallback` is untouched. All of 010-lucy-brand-refresh's existing behavior (voice
reactivity, theme recoloring, reduced-motion, quality-tier degradation, accessibility) is
preserved, and the old ring-generation code is fully removed rather than left alongside the new
implementation.

## Technical Context

**Language/Version**: TypeScript (`~6.0.2`, `strict` mode), React 19.2.7

**Primary Dependencies**: `three` `^0.185.1`, `@react-three/fiber` `^9.6.1`, `@react-three/drei`
`^10.7.7` (all already in use for this exact scene), `simplex-noise` `^4.0.3` (existing, reused
unchanged), **`@react-three/postprocessing` (new — research.md §3)** for the scoped bloom pass

**Storage**: N/A — no backend/database changes; purely a frontend rendering feature

**Testing**: Vitest `^4.1.10` + Testing Library + `jest-axe` (all existing, same as
010-lucy-brand-refresh's testing approach — research.md §7)

**Target Platform**: Web (SPA), WebGL2-capable browsers via the existing `useSceneQualityTier`
gate; `static-fallback` tier (no WebGL2) is unaffected by this feature

**Project Type**: Web application — this feature touches the **frontend only**
(`src/AskLucy.Web/ClientApp`); no backend/API changes

**Performance Goals**: Smooth (no sustained stutter) rendering on the "full" tier at a
substantially higher particle count than today's default; exact particle-count target is
intentionally left directional, not fixed, per spec Clarifications — see research.md §1/§6

**Constraints**: MUST integrate within the existing `SceneBackground` / `ReactiveSphere` /
`useSceneQualityTier` structure (no new top-level scene architecture); bloom effect MUST be
scoped to the sphere only (Clarification Q2); the new dependency MUST be lazy-loaded behind the
existing chat-route `Suspense` boundary, not added to the initial bundle (constitution §7/§15)

**Scale/Scope**: A single reusable visual component family (`ReactiveSphere` + its scene
siblings), rendered once per active chat session; no new user-facing configuration surface

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applicability | Assessment |
|---|---|---|
| I. Clean Architecture & Dependency Rule | N/A | No backend changes; Domain/Application/Infrastructure/Api layers untouched. |
| II. SOLID | Applies | Module split (geometry / shading / animation / post-processing, research.md §6) gives each unit one reason to change; `ReactiveSphere` remains orchestration-only, matching its existing role. |
| III. Simplicity First — DRY/KISS/YAGNI | Applies | No speculative "generic particle engine" abstraction built for a nonexistent second consumer (research.md §6, explicitly rejected as YAGNI); the one new dependency (`@react-three/postprocessing`) is chosen because it's the established, low-complexity way to do scoped bloom in this exact stack, not a bespoke reinvention (research.md §3). |
| IV. Composition Over Inheritance | N/A | Functional React components/hooks throughout; no inheritance introduced. |
| V. Dependency Inversion & Testability | Applies | Geometry generation (`generateFibonacciSpherePositions`) stays a pure, WebGL-free function, unit-testable exactly like its 010 predecessor (research.md §7). |
| VI. Separation of Concerns | Applies | No business logic involved (purely decorative); the module split itself is the separation-of-concerns mechanism for shading/animation/post-processing code. |
| VII. Convention Over Configuration | Applies | Follows the existing `features/chat/scene/` folder convention; no new structural pattern introduced. |
| VIII. No Silent Failures | Applies | Reuses the existing `SceneErrorBoundary` in `SceneBackground.tsx` — a bloom/shader initialization failure on an unusual device must still be caught and logged, falling back to `StaticFallback`, exactly as a shader compile failure would today. Verified as an explicit quickstart.md/task item, not assumed. |
| §7 UI Principles — Accessibility | Applies | Sphere/bloom elements remain inside the existing `aria-hidden="true"` decorative wrapper; no change to that contract (FR-014). |
| §7 UI Principles — Theming | Applies | `dotMeshTheme.ts`'s theme-driven colors are reused unchanged (FR-008); research.md §2 flags a light-mode contrast trade-off to verify, not a theming-contract change. |
| §7 UI Principles — Performance | Applies | New dependency must ride the existing lazy-loaded chat-scene chunk, not the initial bundle — explicit build-output check in quickstart.md. |
| §10 Testing Standards | Applies | New pure logic gets unit tests (research.md §7); behavior changes are covered by updated component/a11y tests; no new test infrastructure (e.g. headless-GL pixel diffing) introduced, matching existing project precedent. |
| §18 AI Coding Agent Rules | Applies | Ambiguities were resolved via `/speckit-clarify` before this plan (spec.md Clarifications), not guessed here. |

**Result**: PASS. No violations requiring Complexity Tracking justification beyond the dependency
note already captured in research.md §3 (treated as staying within the existing three.js/
`@react-three` ecosystem, not a new dependency category, per the spec's own Assumptions).

## Project Structure

### Documentation (this feature)

```text
specs/011-particle-sphere-engine/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

No `contracts/` directory: this feature exposes no external interface (no API endpoint, no CLI,
no public library surface) — it's an internal, presentational React component family whose only
"contract" is `ReactiveSphere`/`SceneBackground`'s existing props (`getReactiveIntensity`,
`qualityTier`, `reducedMotion`), which are unchanged from 010-lucy-brand-refresh.

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/features/chat/scene/
├── ReactiveSphere.tsx                    # orchestration only — wires geometry/material/animation per tier (modified)
├── generateFibonacciSpherePositions.ts   # NEW — replaces generateRingSpherePositions (removed)
├── generateFibonacciSpherePositions.test.ts  # NEW — unit tests (research.md §7)
├── ParticleSphereBloom.tsx               # NEW — scoped EffectComposer/Bloom wrapper (research.md §3), mounted only on 'full' tier
├── sphere.vert.glsl                      # modified — adds uBreath displacement term (research.md §4)
├── sphere.frag.glsl                      # modified — softened falloff (research.md §2)
├── sphereBreath.ts                       # NEW — pure breathing-value helper (research.md §4)
├── sphereBreath.test.ts                  # NEW — unit tests
├── sphereRenderTechnique.ts              # NEW — per-tier blending/bloom derivation (research.md §5)
├── sphereRenderTechnique.test.ts         # NEW — unit tests
├── dotMeshTheme.ts                       # unchanged
├── dotMeshTheme.test.ts                  # unchanged
├── useSceneQualityTier.ts                # unchanged
├── useSceneQualityTier.test.ts           # unchanged
└── SceneBackground.tsx                   # modified — conditionally mounts ParticleSphereBloom on 'full' tier
```

**Structure Decision**: This feature is entirely contained within the existing
`features/chat/scene/` directory (frontend-only, matching the "Web application" project type's
`frontend/` side). No new top-level directory, no backend project touched. New files follow the
existing flat, per-concern file layout already established in this folder by
010-lucy-brand-refresh rather than introducing a nested subfolder — consistent with
constitution §4's "feature/aggregate" folder convention at the granularity this folder already
uses.

## Complexity Tracking

*No unjustified violations — table intentionally omitted per template instructions (fill only if
Constitution Check has violations requiring justification). See Constitution Check's `Result` row
and research.md §3 for the one dependency addition, which is justified there rather than here
since it does not violate a constitution gate.*
