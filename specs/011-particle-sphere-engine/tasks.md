---

description: "Task list for Particle Sphere Rendering Engine Upgrade"
---

# Tasks: Particle Sphere Rendering Engine Upgrade

**Input**: Design documents from `/specs/011-particle-sphere-engine/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [quickstart.md](./quickstart.md)

**Tests**: Included — constitution §10/§18 requires tests for new/changed behavior in the same PR that introduces it; this is not optional for this repository. Per research.md §7, only the pure, WebGL-free logic gets unit tests (matching this codebase's existing precedent); the R3F rendering itself is validated manually via quickstart.md, same as 010-lucy-brand-refresh.

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P3) so each can be implemented, tested, and shipped independently. All paths are relative to `src/AskLucy.Web/ClientApp/src/` unless stated otherwise.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 (visual fidelity to the reference image), US2 (idle rotation + breathing), US3 (device/quality-tier behavior)

---

## Phase 1: Setup

**Purpose**: Add the one new dependency this feature needs before any story references it.

- [X] T001 Add `@react-three/postprocessing` (and its `postprocessing` peer dependency) to `dependencies` in `src/AskLucy.Web/ClientApp/package.json` and run `npm install` (research.md §3)

**Checkpoint**: Dependency installed and importable.

---

## Phase 2: Foundational

**Purpose**: Blocking prerequisites shared by *all* user stories.

**None** — unlike 010-lucy-brand-refresh's four disjoint areas, this feature's three stories all
incrementally extend the same small file set (`ReactiveSphere.tsx`, `sphere.vert.glsl`,
`sphere.frag.glsl`, `SceneBackground.tsx`) rather than standing up shared infrastructure first,
per plan.md's Constitution Check (§2.III Simplicity/YAGNI). US1 does the foundational geometry
swap as its own first task (it's FR-001, US1's defining requirement); US2 and US3 build directly
on US1's rewritten files — see Dependencies below.

---

## Phase 3: User Story 1 — A dot mesh that actually looks like the reference (Priority: P1) 🎯 MVP

**Goal**: The sphere renders with a uniform Fibonacci-distributed point spread, soft glowing
additive-blended particles, and a scoped neon halo — matching the reference image instead of the
current ring-patterned, flat-dot look.

**Independent Test**: Per spec.md — open the AI workspace and visually compare the rendered
sphere against the reference image, confirming uniform spread and a soft glow; see
quickstart.md "User Story 1."

### Tests for User Story 1

> Write these first; they must fail before the corresponding implementation task.

- [X] T002 [P] [US1] Unit tests for `generateFibonacciSpherePositions(totalPoints, radius)` covering output length (`totalPoints * 3`), constant distance-from-origin per point (within float tolerance), and no two consecutive points sharing identical coordinates, in `features/chat/scene/generateFibonacciSpherePositions.test.ts` (research.md §7, data-model.md `SpherePositions`)

### Implementation for User Story 1

- [X] T003 [P] [US1] Implement `generateFibonacciSpherePositions(totalPoints, radius): Float32Array` using the golden-angle sampler in `features/chat/scene/generateFibonacciSpherePositions.ts` (research.md §1; makes T002 pass)
- [X] T004 [US1] Remove `generateRingSpherePositions` and its call site from `features/chat/scene/ReactiveSphere.tsx`; call `generateFibonacciSpherePositions` instead (depends on T003; satisfies FR-001, begins FR-013)
- [X] T005 [P] [US1] Soften the circular point-sprite alpha falloff in `features/chat/scene/sphere.frag.glsl` for a smoother glowing edge (research.md §2; satisfies FR-002)
- [X] T006 [US1] Set `blending: THREE.AdditiveBlending` (with the existing `depthWrite={false}`) on the `<shaderMaterial>` in `features/chat/scene/ReactiveSphere.tsx` (depends on T004, T005; satisfies FR-003 — overlap brightening falls out of additive blending with no extra shader logic)
- [X] T007 [US1] Create `features/chat/scene/ParticleSphereBloom.tsx` — an `<EffectComposer>` + selective `<Bloom>` wrapper using `@react-three/postprocessing`'s layer-based selection so only the particle sphere blooms — and mount it in `features/chat/scene/SceneBackground.tsx` alongside `ReactiveSphere` (depends on T001; satisfies FR-004 — manually verify per quickstart.md "User Story 1" step 4 that no other scene element, e.g. ambient light or camera controls, blooms)
- [X] T008 [US1] Manually verify additive blending still reads as an intentional neon color (not washed out) against `dotMeshTheme.ts`'s light-mode palette; adjust idle/reactive color darkness only if the check fails (research.md §2's flagged trade-off)
- [X] T009 [US1] Manually verify `SceneErrorBoundary` in `SceneBackground.tsx` still catches a thrown failure from the new bloom/shader path and falls back to `StaticFallback` (constitution §2.VIII; temporarily throw inside `ParticleSphereBloom` during manual testing, then revert)

**Checkpoint**: User Story 1 is independently functional — on the default desktop ("full" tier) rendering, the sphere visually matches the reference image.

---

## Phase 4: User Story 2 — A calm, living presence (Priority: P2)

**Goal**: The sphere continuously rotates and exhibits a subtle breathing pulse while idle, both
layering on top of (never interrupting) the existing voice-reactive deformation.

**Independent Test**: Per spec.md — watch the idle sphere and confirm continuous rotation plus a
repeating breathing pulse, then confirm voice-reactive deformation still layers on top correctly;
see quickstart.md "User Story 2."

### Tests for User Story 2

- [X] T010 [P] [US2] Unit tests for a pure `computeBreathValue(elapsedSeconds, frequency, amplitude)` helper covering periodicity and amplitude bounds, in `features/chat/scene/sphereBreath.test.ts`

### Implementation for User Story 2

- [X] T011 [P] [US2] Implement `computeBreathValue(elapsedSeconds, frequency, amplitude): number` (simple sine) in `features/chat/scene/sphereBreath.ts` (makes T010 pass)
- [X] T012 [US2] Add a `uBreath` uniform to `features/chat/scene/sphere.vert.glsl` and fold it additively into the existing per-point radial `displacement` alongside the noise/reactive term (research.md §4; depends on T004 — edits the vertex shader US1 already rewrote; satisfies FR-006)
- [X] T013 [US2] Wire `computeBreathValue()` into `ReactiveSphere.tsx`'s `useFrame`, writing the result into the new `uBreath` uniform every frame, and hold it at `0` when `reducedMotion` is true alongside the existing amplitude/frequency freeze (depends on T011, T012; satisfies FR-006/FR-011)
- [X] T014 [US2] Manually verify continuous idle rotation (FR-005) and breathing (Acceptance Scenario 1/2) per quickstart.md "User Story 2" step 1, then verify Acceptance Scenario 3 — breathing and voice-reactive deformation layer together without either interrupting the other — per step 2

**Checkpoint**: User Stories 1 AND 2 both work independently — the sphere looks right and idles with rotation + breathing, correctly layering with voice reactivity.

---

## Phase 5: User Story 3 — The richer visual holds up across devices (Priority: P3)

**Goal**: The "full" tier renders at a substantially higher particle count than today while the
"reduced" tier deliberately falls back to a simpler, non-glowing, non-bloomed technique — both
continuing to respect reduced-motion and the existing performance-downgrade ratchet.

**Independent Test**: Per spec.md — load the workspace on both a high-end and a throttled/low-end
device profile and confirm the high-end profile is visibly denser/richer while the low-end
profile still renders a recognizable, simpler sphere without dropping below the platform's
acceptable frame rate; see quickstart.md "User Story 3."

### Tests for User Story 3

- [X] T015 [P] [US3] Unit tests for `getSphereRenderTechnique(qualityTier)` covering `'full'` → additive blending + bloom enabled, `'reduced'` → normal blending + bloom disabled, in `features/chat/scene/sphereRenderTechnique.test.ts` (data-model.md `SphereRenderTechnique`)

### Implementation for User Story 3

- [X] T016 [P] [US3] Implement `getSphereRenderTechnique(qualityTier: 'full' | 'reduced')` in `features/chat/scene/sphereRenderTechnique.ts` (research.md §5; makes T015 pass)
- [X] T017 [US3] Update `ReactiveSphere.tsx` to call `getSphereRenderTechnique(qualityTier)` and apply its `blending` result to the `<shaderMaterial>`, replacing US1 T006's unconditional `AdditiveBlending` with the tier-derived value (depends on T016; satisfies FR-010's blending half)
- [X] T018 [US3] Update `SceneBackground.tsx` to mount `ParticleSphereBloom` only when `getSphereRenderTechnique(qualityTier).bloomEnabled` is true, replacing US1 T007's unconditional mount (depends on T016, T007; satisfies FR-004/FR-010's bloom scoping)
- [X] T019 [US3] Tune the "full"-tier point-count constant in `ReactiveSphere.tsx` to a substantially higher value than 010's defaults (`full: 1400`) while remaining smooth on capable desktop hardware, per manual perf check in quickstart.md (satisfies FR-009/SC-002; exact number is an implementation choice per spec Clarifications, not fixed)
- [X] T020 [US3] Manually verify the existing one-way performance-regression downgrade (`useSceneQualityTier`'s `reportPerformanceRegression`) still triggers under sustained frame-time regression and steps the sphere down to the "reduced" tier's simpler technique (T017/T018) — satisfies FR-012
- [X] T021 [US3] Manually verify the `'static-fallback'` tier (`SceneBackground.tsx`'s `StaticFallback`) is unaffected by this feature's changes — satisfies FR-010's static-fallback continuity

**Checkpoint**: All three user stories are independently functional; "full" vs. "reduced" tier behavior is fully differentiated.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Regression safety and final gates across all three stories.

- [X] T022 [P] Run 010-lucy-brand-refresh's existing quickstart checks (voice-reactive behavior, theme recoloring, reduced-motion, quality-tier degradation) per this feature's quickstart.md "Regression pass against 010-lucy-brand-refresh" — satisfies SC-003
- [X] T023 [P] Code review pass confirming zero leftover code from the old `generateRingSpherePositions`/ring implementation, and that the "full"/"reduced" tier techniques (T016) are the only two intentional rendering code paths — satisfies FR-013/SC-004
- [X] T024 Re-run `ChatPage.a11y.test.tsx` to confirm zero new accessibility violations introduced by `ParticleSphereBloom` — satisfies FR-014
- [X] T025 Verify via `npm run build` output that `@react-three/postprocessing` only inflates the lazy-loaded chat-route chunk, not the initial/auth bundle — satisfies constitution §7/§15
- [X] T026 Run `npm test`, `npm run lint`, and `npm run build` in `src/AskLucy.Web/ClientApp` and fix any failures (constitution §12 CI/CD gates 1–3, checked locally before PR)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: None (see note above) — proceed directly to Phase 3.
- **User Story 1 (Phase 3)**: Depends on Setup (T001, for T007's bloom dependency) — otherwise unblocked.
- **User Story 2 (Phase 4)**: Depends on US1's `ReactiveSphere.tsx`/`sphere.vert.glsl` rewrite (T004) — not independently startable before US1 lands, but independently *testable* once it does (rotation/breathing don't depend on US1's glow/bloom work, only on the file existing in its post-T004 shape).
- **User Story 3 (Phase 5)**: Depends on US1's material/bloom-mount tasks (T006, T007) to have something to make tier-conditional — same "builds on, independently testable" relationship as US2.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### Within Each User Story

- Tests are written and MUST fail before the corresponding implementation task.
- Pure logic (geometry/breath/technique functions) before the component wiring that consumes it.
- Story complete (checkpoint) before moving to the next priority.

### Parallel Opportunities

- T002 (US1 test) and T005 (frag shader) can run in parallel with each other and with T003 (different files, no shared dependency until T004/T006 combine them).
- T010 (US2 test) and T011 (breath helper) can run in parallel with US1's later manual-verification tasks (T008/T009) once US1's core files (T004) have landed.
- T015 (US3 test) and T016 (technique function) can run in parallel with US2's tasks once US1 has landed, since they touch a new file (`sphereRenderTechnique.ts`) independent of US2's `sphereBreath.ts`.
- T022 and T023 in Polish can run in parallel (different concerns, no shared file).

---

## Parallel Example: User Story 1

```bash
# Launch in parallel:
Task: "Unit tests for generateFibonacciSpherePositions in features/chat/scene/generateFibonacciSpherePositions.test.ts"
Task: "Soften the circular point-sprite alpha falloff in features/chat/scene/sphere.frag.glsl"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001).
2. Complete Phase 3: User Story 1 (T002–T009).
3. **STOP and VALIDATE**: Run quickstart.md "User Story 1" against a real desktop browser.
4. Demo if ready — this alone closes the visual-fidelity gap that motivated the feature.

### Incremental Delivery

1. Setup → User Story 1 → validate → demo (MVP: the sphere finally looks right).
2. Add User Story 2 → validate rotation/breathing/reactive layering → demo.
3. Add User Story 3 → validate tier differentiation and performance → demo.
4. Polish (Phase 6) → full regression pass against 010-lucy-brand-refresh → ship.

---

## Notes

- [P] tasks touch different files with no unfinished dependency between them.
- [Story] labels map every implementation/test task back to spec.md's US1–US3 for traceability.
- Per research.md §7, GLSL/visual correctness is validated manually via quickstart.md, not via
  new headless-GL test infrastructure — consistent with this codebase's existing precedent.
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently before continuing.
