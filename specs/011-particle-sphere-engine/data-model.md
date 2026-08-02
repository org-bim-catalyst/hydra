# Phase 1 Data Model: Particle Sphere Rendering Engine Upgrade

This feature makes no backend/database changes (Constitution Check: N/A for §3/§5). "Entities"
below are frontend-only TypeScript data shapes internal to the rendering feature — config/derived
render state, not persisted records. Nothing here is new user data; all of it derives from the
existing `qualityTier`/`reducedMotion`/`mode` inputs `ReactiveSphere` already receives (spec
Assumptions: no new user-facing controls).

## SpherePositions

Output of `generateFibonacciSpherePositions(totalPoints, radius)`
(`features/chat/scene/generateFibonacciSpherePositions.ts`), replacing 010's
`generateRingSpherePositions`.

| Field | Type | Notes |
|---|---|---|
| return value | `Float32Array` | Flat `[x0,y0,z0, x1,y1,z1, ...]`, length `totalPoints * 3` — same shape contract as the function it replaces, so the `<bufferAttribute attach="attributes-position">` call site in `ReactiveSphere.tsx` is unchanged. |
| `totalPoints` (param) | `number` | Comes from a per-tier point-count constant (research.md §5/§6 — exact "full"-tier number is an implementation/tuning decision per spec Clarifications, not fixed here). |
| `radius` (param) | `number` | Unchanged: `SPHERE_RADIUS` constant, same as today. |

**Validation rule**: for `totalPoints >= 2`, every point lies at distance `radius` from the
origin (within floating-point tolerance) and no two consecutive indices `i`/`i+1` produce
identical `(x,y,z)` — covered by `generateFibonacciSpherePositions.test.ts` (research.md §7).

## SphereRenderTechnique

Not a literal exported type necessarily, but the decision point `ReactiveSphere.tsx`/
`SceneBackground.tsx` branch on per `qualityTier` — documented here as the shape of that
decision (research.md §5).

| Field | Type | Notes |
|---|---|---|
| `qualityTier` | `'full' \| 'reduced'` (existing `SceneQualityTier` minus `'static-fallback'`, which never mounts this component) | Same prop `ReactiveSphere` already receives — no new prop. |
| `blending` | derived, not stored | `THREE.AdditiveBlending` when `qualityTier === 'full'`; `THREE.NormalBlending` (three.js default) when `'reduced'` (research.md §2/§5). |
| `bloomEnabled` | derived, not stored | `true` only when `qualityTier === 'full'` — gates whether the sphere is added to the bloom-selection layer and whether `SceneBackground.tsx` mounts `ParticleSphereBloom` at all (research.md §3/§5). |

**State transition**: driven entirely by the existing `useSceneQualityTier`'s one-way
`full → reduced` ratchet (010-lucy-brand-refresh, unchanged) — when performance regression
triggers a downgrade, `bloomEnabled`/`blending` recompute from the new `qualityTier` on next
render; no separate state machine is introduced.

## SphereAnimationUniforms (extension)

Extends 010's existing shader uniform set (`ReactiveSphere.tsx`'s `uniforms` object) with one new
field; all others are unchanged in name/meaning.

| Field | Type | Notes |
|---|---|---|
| `uTime` | `{ value: number }` | Unchanged. |
| `uAmplitude` | `{ value: number }` | Unchanged (idle vs. voice-reactive, per 010). |
| `uFrequency` | `{ value: number }` | Unchanged. |
| `uBreath` *(new)* | `{ value: number }` | `sin(elapsed * BREATH_FREQUENCY) * BREATH_AMPLITUDE`, written every frame in `useFrame` alongside the existing uniform writes (research.md §4); frozen (held at `0`) when `reducedMotion` is true, per FR-011. |
| `uBasePointSize` | `{ value: number }` | Unchanged. |
| `uColorIdle` / `uColorReactive` | `{ value: THREE.Color }` | Unchanged (010's theme-driven colors, FR-008). |

**Validation rule**: `uBreath`'s contribution to per-point displacement MUST be additive with the
existing noise/reactive displacement in `sphere.vert.glsl`, never a replacement of it — this is
what makes User Story 2's Acceptance Scenario 3 (breathing + reactive deformation coexist) true
by construction rather than by extra branching logic.

## Relationship to 010-lucy-brand-refresh's existing entities

`DotMeshThemeColors` (`dotMeshTheme.ts`) and the `Quality Tier` concept itself are unchanged and
reused as-is (FR-008/FR-010/FR-012) — not re-documented here; see
`specs/010-lucy-brand-refresh/data-model.md`.
