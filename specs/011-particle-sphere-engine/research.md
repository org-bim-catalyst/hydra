# Phase 0 Research: Particle Sphere Rendering Engine Upgrade

## §1. Uniform particle distribution (replacing the ring lattice)

**Decision**: Replace `generateRingSpherePositions` (stratified latitude rings, no per-point
rotation) with a standard **golden-angle Fibonacci sphere** sampler: for `i` in `[0, N)`,
`y = 1 - (i / (N - 1)) * 2`, `radius = sqrt(1 - y*y)`, `theta = i * goldenAngle` (`goldenAngle =
π * (3 - sqrt(5))`), point = `(cos(theta) * radius, y, sin(theta) * radius) * sphereRadius`. This
is a pure, allocation-light function with the same signature shape as today's
`generateRingSpherePositions(totalPoints, radius): Float32Array`, so it's a drop-in replacement
at the call site in `ReactiveSphere.tsx`.

**Rationale**: This is the textbook approach for even point-on-sphere distribution with O(N) cost
and no iterative relaxation — every point gets a unique latitude band *and* a golden-angle
azimuthal offset, which is exactly what eliminates the visible ring banding FR-001 calls out
(the current function's flaw is that every point in a given ring shares the same discrete band
with evenly-spaced-by-index azimuth, producing visible "seams"; a per-point golden-angle offset
removes that structure). It's also the exact algorithm named in both the user-supplied reference
image's likely construction and the attached reference implementation's `createFibonacciSphere`
function — confirming it as the correct technique, not just a convenient one.

**Alternatives considered**:
- *Keep ring-grouped sampling, just add more rings* — rejected: more rings narrows band width but
  never removes banding entirely; it's the wrong primitive for "uniform," not a tuning problem.
- *Poisson-disc / blue-noise relaxation sampling* — rejected: substantially more expensive to
  generate (iterative), and unnecessary — Fibonacci sphere sampling is already visually uniform
  enough for a particle-sphere effect at any particle count this feature targets; the extra
  uniformity Poisson-disc buys isn't perceptible at point-sprite scale.
- *Uniform random scatter* — rejected (carried over from 010's research.md §1): still clusters
  and leaves visible gaps at any finite N, which is the opposite of FR-001.

## §2. Soft glow, additive blending, and overlap brightening

**Decision**: Keep the existing per-point circular-sprite discard in `sphere.frag.glsl`
(`smoothstep(0.5, 0.3, dist)`) but widen/soften the falloff further, and set
`blending: THREE.AdditiveBlending` (with `depthWrite: false`, already set) on the "full" tier's
`ShaderMaterial`. Additive blending is what makes FR-003 (overlap brightening) fall out for free —
overlapping fragments' colors sum in the framebuffer rather than the nearer point occluding the
one behind it — with zero additional per-fragment logic required.

**Rationale**: This directly satisfies FR-002/FR-003 with a shader-parameter change to code that
already exists, not a rewrite. Additive blending is the standard technique for "energy/light"
particle effects for exactly this reason (light adds, it doesn't occlude).

**Known trade-off carried into tasks.md**: additive blending over a *light*-theme (near-white)
background washes out toward white rather than reading as a bright color, unlike over dark
backgrounds where it reads as neon. `dotMeshTheme.ts`'s light-mode colors (research.md §2 of
010-lucy-brand-refresh: `primary.dark`/`secondary.dark`) were already chosen for contrast against
a light background pre-additive-blending; implementation MUST manually verify additive blending
still reads as intentional (not washed-out) in light mode against `StaticFallback`'s light
gradient, and adjust idle/reactive color darkness or per-theme opacity if it doesn't — this is a
visual tuning task, not an open spec question (FR-008 already requires the palette swap to keep
working; this is *how* it keeps looking good once blending changes).

**Alternatives considered**:
- *Normal (non-additive) blending with a manually brightened overlap shader* — rejected: would
  require detecting overlap in-shader (not straightforward for unordered point sprites with no
  neighbor information) to get the same effect additive blending gives for free.

## §3. Scoped neon glow / bloom

**Decision**: Add `@react-three/postprocessing` (thin R3F wrapper around the `postprocessing`
npm package) and use its selective-bloom pattern — wrap only the particle sphere's `<points>` in
drei/postprocessing's layer-based selection (rendering the sphere on a dedicated `THREE.Layers`
bit that the `<EffectComposer><Bloom/></EffectComposer>` pass is configured to isolate) — rather
than a scene-wide bloom pass. `EffectComposer`/`Bloom` mount as siblings of `ReactiveSphere` and
`OrbitControls` inside `SceneBackground.tsx`'s existing `<Canvas>`, active only on the "full" tier.

**Rationale**: A real screen-space bloom pass (bright-pixel extraction + blur + additive
composite) is what actually produces the reference image's soft light-bleed "halo" around the
sphere — the flat, non-blended point sprites in the user's attached reference implementation
(`ring sphere.html`) don't have this at all, which is itself a large part of why that file reads
as flatter than the target image; §2's additive blending alone gives per-particle glow but not
the soft *ambient* halo around the sphere as a whole that FR-004 and User Story 1's "neon halo"
call for. `@react-three/postprocessing` is the established, actively-maintained (pmndrs, same
org as `@react-three/fiber`/`drei` already in this project) way to do this in an R3F app — hand-
rolling an `EffectComposer`/render-target pipeline directly against `three` would duplicate what
this library already solves and risks fighting R3F's own render loop (constitution §2.III KISS:
prefer the simplest design that satisfies the requirement, which here means the established
library, not a bespoke pass). Layer-based selective bloom is the standard mechanism this
ecosystem provides for "bloom this object, not the rest of the scene," which is exactly
Clarification Q2's requirement — it doesn't need per-frame manual scene-graph filtering.

**Dependency footprint note**: this adds one new runtime package (plus its `postprocessing` peer
dependency) to the frontend. This is treated as staying within the *existing* three.js/`@react-
three` ecosystem already used for this exact scene (not a new rendering framework or a new
dependency *category*, which is what the spec's Assumptions section rules out) — it's the same
relationship `@react-three/drei` already has to `@react-three/fiber`. It MUST be imported only
from within `features/chat/scene/` so it rides the existing lazy-loaded chunk boundary
(`SceneBackground` is already behind a `Suspense` boundary per `ChatPage.tsx`) rather than
inflating any other route's bundle (constitution §7/§15).

**Alternatives considered**:
- *Faux glow via a layered, larger, lower-opacity additive "halo" points pass (no post-
  processing library, no new dependency)* — seriously considered and rejected only narrowly: it
  is cheaper and dependency-free, and remains the fallback if `@react-three/postprocessing`
  proves incompatible with a WebGL2-but-limited device in practice (see edge case in spec.md:
  "browser supports WebGL2 but not the glow technique" — degrade to this faux-glow layer, or no
  glow, rather than failing to render). Rejected as the *primary* approach because it cannot
  reproduce genuine HDR-style light bleed into surrounding dark pixels the way a real bloom pass
  does, and the user explicitly named "bloom post-processing" as a required technique after
  multiple prior attempts already fell short — a synthetic approximation risks the same "doesn't
  match the reference" outcome for the exact same visual reason as before.
- *Whole-canvas (non-selective) bloom* — rejected per Clarification Q2 (sphere-only).
- *Hand-rolled `EffectComposer` + raw `three/examples/jsm` `UnrealBloomPass`, outside
  `@react-three/postprocessing`* — rejected: works with vanilla `three`, but integrating it
  correctly with R3F's own render loop (which owns the default render call) requires manually
  taking over rendering via `useFrame`'s `render` priority override — exactly the integration
  work `@react-three/postprocessing` already provides, tested, for this stack.

## §4. Breathing animation

**Decision**: Add a `uBreath` uniform to `sphere.vert.glsl`, computed once per frame on the CPU
as a simple sine (`Math.sin(elapsed * BREATH_FREQUENCY) * BREATH_AMPLITUDE`, both small constants
distinct from `IDLE_AMPLITUDE`/`IDLE_FREQUENCY`) and added to the existing per-point radial
`displacement` alongside the noise term, rather than scaling the whole `<group>` via
`groupRef.scale`. Idle rotation stays exactly as it is today (`groupRef.current.rotation.y +=`
in `useFrame`).

**Rationale**: Folding breathing into the existing vertex-shader displacement keeps the GPU doing
the per-point work (matching the feature description's "GPU-based animation whenever possible")
and composes naturally with the existing noise-driven idle wobble and the voice-reactive
amplitude — all three additively drive the same `displacement` value already computed per-vertex,
so User Story 2's "breathing layers on top of reactive deformation without either interrupting
the other" (Acceptance Scenario 3) is true by construction, not by extra coordination logic.
Object-level rotation is left as-is because it's a single 4×4 matrix update per frame regardless
of particle count — negligible cost, no reason to move it.

**Alternatives considered**:
- *`groupRef.scale.setScalar(1 + breathValue)`* — rejected: uniformly scaling the whole group
  would also scale the glow/bloom halo's apparent size discontinuously relative to the particle
  positions used for the (fixed-radius) selective-bloom pass, and is a CPU-side transform rather
  than the GPU-based approach the feature explicitly asks for.

## §5. "Reduced" tier: a deliberately different, simpler technique

**Decision**: Per Clarification Q3, the "reduced" tier keeps using the *current* (pre-this-
feature) rendering approach conceptually — soft circular point sprites, normal (non-additive)
blending, no selective bloom pass — at its existing lower point count, rather than the "full"
tier's new additive+bloom shader path. Concretely: the "full" and "reduced" tiers use the same
`sphere.vert.glsl` (position/displacement math is identical and cheap at any tier) but the
"reduced" tier's `<shaderMaterial>` omits `blending: AdditiveBlending` and the sphere is not
added to the bloom-selection layer, so the `<EffectComposer>` never processes it.

**Rationale**: Satisfies FR-010 exactly as clarified, and keeps the two tiers as one shared
geometry/animation module with only material-level and composition-level differences — not two
parallel particle systems — which is what FR-013's "no parallel/duplicate implementation"
requires. The expensive parts (bloom pass, additive overdraw) are the parts skipped, which is
also where the actual GPU cost of the "full" tier's richer look lives.

**Alternatives considered**:
- *Same shader/blending/bloom on "reduced," just fewer particles* — this was the initial
  recommendation before clarification; rejected by the user in favor of the simpler-technique
  approach, which better serves constrained/mobile devices where the bloom pass itself (an
  extra full-screen render target + blur, independent of particle count) is the more expensive
  part to avoid, not just point count.

## §6. Module structure and cleanup

**Decision**: Restructure `features/chat/scene/` into clearly separated, independently testable
modules along the seams the spec's Assumptions already call for:

- `generateFibonacciSpherePositions.ts` — pure geometry function (§1), unit-testable without
  WebGL/canvas, replacing `generateRingSpherePositions` (removed — FR-013).
- `sphere.vert.glsl` / `sphere.frag.glsl` — shading, extended per §2/§4 (kept as the existing
  `.glsl` files, not a new format).
- `useSphereBreath.ts` or inlined in `ReactiveSphere.tsx` (exact extraction point decided in
  tasks.md based on resulting complexity) — animation/uniform-update logic per §4.
- `ParticleSphereBloom.tsx` — the `<EffectComposer>`/selective-`<Bloom>` wrapper (§3), mounted
  conditionally by `SceneBackground.tsx` only for `qualityTier === 'full'`.
- `ReactiveSphere.tsx` — orchestration only: wires geometry + material + animation together per
  tier, as it does today.

`dotMeshTheme.ts`, `useSceneQualityTier.ts`, and `SceneBackground.tsx`'s overall structure are
unchanged in shape (per FR-007/008/010-012, their existing contracts are preserved).

**Rationale**: This is the existing `features/<domain>` folder convention (constitution §4)
applied at finer grain, matching the spec's Assumptions ("clean separation between geometry,
shaders, animation, and post-processing" = module boundaries, not a new architectural pattern).
Each new/changed unit remains a plain function or a small component with a narrow prop surface,
satisfying constitution §2.II (SRP) and §2.V (unit-testable without a real WebGL context for the
pure parts).

**Alternatives considered**:
- *Add a generic "particle engine" abstraction (config-driven, reusable for hypothetical future
  particle effects beyond this one sphere)* — rejected per constitution §2.III (YAGNI): no second
  consumer exists or is specified; "modular, reusable" in the spec's Assumptions is satisfied by
  clean internal separation, not by building a speculative public API for effects that don't
  exist yet.

## §7. Testing approach for shader/visual code

**Decision**: Unit-test the pure, non-WebGL parts exactly as 010 did for
`generateRingSpherePositions`'s successor equivalents:
`generateFibonacciSpherePositions.test.ts` (point count, radius bounds, distribution sanity
— e.g. no two consecutive points share identical `(x,y,z)`, min/max latitude coverage) and any
extracted breathing-value helper. Shader source (`.glsl`) and the bloom composition itself are
validated by the existing `ChatPage.a11y.test.tsx`-style suite (renders without throwing,
`SceneErrorBoundary` still catches failures) plus the manual `quickstart.md` visual pass — GLSL
correctness at the pixel level is not unit-testable in `jsdom`/CI, consistent with how the
existing shader code is already (not) tested today.

**Rationale**: Matches the existing test boundary in this codebase (pure logic gets unit tests;
WebGL rendering output gets manual/visual validation) rather than introducing new tooling (e.g. a
headless-GL pixel-diffing setup) that nothing else in the project uses — constitution §2.III
KISS/YAGNI again.

**Alternatives considered**:
- *Headless-GL pixel snapshot testing* — rejected: real value (catching visual regressions in
  CI) but a large new testing-infrastructure investment with no existing precedent in this
  codebase; out of scope for this feature per YAGNI, revisit only if visual regressions in this
  area recur often enough to justify it.
