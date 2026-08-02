# Quickstart: Validating the Particle Sphere Rendering Engine Upgrade

Manual + automated validation for the three user stories. Run from
`src/AskLucy.Web/ClientApp`. Requires a desktop browser with WebGL2 support for the visual
checks (the same requirement 010-lucy-brand-refresh's sphere already has).

## Prerequisites

- Frontend dependencies installed (`npm install`, including the new
  `@react-three/postprocessing` dependency added by this feature — see research.md §3).
- The reference image and the user-supplied `ring sphere.html` reference implementation on hand
  for the side-by-side visual comparison in User Story 1.

## Setup

```bash
npm run dev
```

Open the printed local URL, sign in, and open the chat workspace.

## User Story 1 — Matches the reference

1. With the chat workspace open and the assistant idle, visually compare the rendered sphere
   against the reference image: confirm dots are spread evenly across the whole sphere surface
   with no visible ring banding or polar clustering (FR-001, SC-001).
2. Confirm each dot has a soft glowing edge (not a flat hard-edged circle) and the sphere as a
   whole shows a visible neon-style glow/halo (FR-002/FR-004).
3. Rotate the camera (via the existing `OrbitControls`) to a viewing angle where dots visually
   overlap; confirm the overlapping area appears brighter than a single dot, not simply occluded
   (FR-003).
4. Confirm the glow does **not** bleed onto anything else in the scene — there is currently
   nothing else to visually bloom, so this is confirmed by inspecting that no unrelated
   brightening artifacts appear at the canvas edges or around the ambient light (FR-004
   scoping — Clarification Q2).
5. Automated: `npm test -- generateFibonacciSpherePositions` (new unit tests, research.md §7).

## User Story 2 — Calm, living presence

1. With the assistant idle, watch the sphere for at least 10 seconds: confirm continuous slow
   rotation (FR-005) and a subtle, repeating breathing-style pulse distinct from the rotation
   (FR-006).
2. Trigger an assistant voice reply (as in 010's quickstart User Story 2); confirm the existing
   voice-reactive deformation still occurs, visibly layered on top of the idle rotation/breathing
   rather than replacing it (FR-007, Acceptance Scenario 3).
3. Toggle light/dark mode while the sphere is mid-animation; confirm the dot color transition
   completes with no flicker or mismatched-theme flash (FR-008, carried from 010 SC-004) — pay
   particular attention to whether the additive-blended glow still reads clearly (not washed out)
   in light mode (research.md §2's noted trade-off).
4. Enable OS-level "reduce motion"; confirm idle rotation and breathing both freeze and reactive
   intensity stays capped (FR-011).
5. Automated: `npm test -- sphereBreath` (new unit tests for the breathing helper).

## User Story 3 — Holds up across devices

1. On a capable desktop browser, confirm the sphere renders at a substantially higher particle
   density than 010's default (visibly denser/"dust-like"), staying smooth with no stutter
   (FR-009, SC-002).
2. Resize the browser below the mobile breakpoint (or use device emulation) to force the
   `'reduced'` quality tier; confirm the sphere still renders as a recognizable dot sphere using
   its own simpler, non-glowing/non-bloomed technique (FR-010) — not the "full" tier's look at a
   lower count, and not a blank space.
3. Force the `'static-fallback'` tier (disable WebGL2 or use a throttled devtools GPU override);
   confirm the existing themed static placeholder still renders unchanged.
4. If feasible, simulate sustained frame-rate regression on a "full"-tier session (e.g. via
   devtools CPU throttling) and confirm the existing one-way performance monitor still downgrades
   to the "reduced" tier's simpler technique (FR-012).
5. Automated: `npm test -- sphereRenderTechnique` (new unit tests for the per-tier
   blending/bloom derivation) and re-run `ChatPage.a11y.test.tsx` to confirm no new accessibility
   violations from the added `ParticleSphereBloom` element (FR-014 — still exposed as decorative).

## Regression pass against 010-lucy-brand-refresh

1. Re-run 010's own quickstart User Story 2 checks (`specs/010-lucy-brand-refresh/quickstart.md`)
   end-to-end — voice-reactive behavior, theme recoloring, reduced-motion, quality-tier
   degradation must all still pass unchanged (SC-003).
2. Code review: confirm `generateRingSpherePositions` and any dead code/assets tied only to the
   old ring implementation have been removed, not left alongside the new implementation
   (FR-013, SC-004).

## Full regression pass

```bash
npm test
npm run lint
npm run build
```

All three MUST pass before considering the feature done (constitution §12 CI/CD gates 1–3 apply
locally as a pre-check). Confirm via the production build output that
`@react-three/postprocessing` only inflates the chat route's lazy-loaded chunk, not the
initial/auth bundle (constitution §7/§15 — lazy-load large dependencies behind the feature that
needs them).
