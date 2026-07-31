# Phase 0 Research: Lucy Brand & Voice Refresh

## §1. Dot-mesh sphere rendering approach

**Decision**: Replace `ReactiveSphere`'s `<mesh><icosahedronGeometry/></mesh>` (a continuous
shaded surface) with `<points><bufferGeometry/></points>` (`THREE.Points`), sampling point
positions on a sphere using a **Fibonacci sphere lattice** grouped into concentric latitude
rings (rather than uniform random scatter), to match the reference image's visible "orbital
ring" banding. Vertex-shader displacement keeps the same simplex-noise approach as today
(`sphere.vert.glsl`), just applied per-point instead of per-mesh-vertex; `gl_PointSize` is
attenuated by camera distance so dots stay a consistent visual size. The fragment shader
draws each point as a soft circular sprite (discard outside a radius in point-sprite UV
space) instead of Lambertian surface shading.

**Rationale**: `THREE.Points` is the standard three.js primitive for large sprite-based point
clouds and is already available via the `three` dependency already in the bundle (no new
package). A Fibonacci lattice is the simplest well-distributed sphere-sampling method that
still reads as "rings" when banded by latitude, avoiding a bespoke ring-generation algorithm.
Reusing the existing noise-driven vertex displacement preserves `ReactiveSphere`'s established
idle/reactive animation contract (`uAmplitude`/`uFrequency`/`uTime`) so `SceneBackground`,
`useSceneQualityTier`, and the TTS-intensity wiring (`getReactiveIntensity`) need no changes.

**Alternatives considered**:
- *Instanced small spheres/`InstancedMesh`* — rejected: far more draw overhead per dot than
  `THREE.Points` for the same visual result; no benefit here since dots don't need individual
  3D geometry, only a 2D circular sprite facing the camera.
- *Uniform random point scatter* — rejected: doesn't produce the reference image's visible
  concentric-ring banding; a Fibonacci lattice grouped by latitude band gives ring structure
  "for free" from the sampling method itself.
- *Post-process/screen-space dot shader over the existing solid mesh* — rejected: more complex
  (extra render pass) for a less faithful "sphere made of dots" result than directly replacing
  the geometry.

## §2. Theme-reactive dot colors

**Decision**: Add `features/chat/scene/dotMeshTheme.ts` exporting a pure function
`getDotMeshColors(mode: ThemeMode): { idle: string; reactive: string }` that derives from the
*existing* palette tokens in `theme/tokens/palette.ts` rather than introducing new colors:

| Mode | Idle (was fixed `#1F4E5E`) | Reactive (was fixed `#B8461F`) |
|---|---|---|
| light | `primary.dark` (`#123340`) | `secondary.dark` (`#7E2E12`) |
| dark | `primary.light` (`#4C7B8B`) | `secondary.light` (`#D97650`) |

`ReactiveSphere` reads the current mode from `useThemeStore` and re-derives these colors
whenever it changes, updating the shader material's `uColorIdle`/`uColorReactive` uniforms in
place (no material re-creation, no remount) so the transition is a single-frame uniform write.

**Rationale**: The current sphere already hardcodes `uColorIdle`/`uColorReactive` as literal
hex — identical in both themes today, which is itself the theming gap the spec calls out
(constitution §7: "components MUST NOT hardcode colors that bypass the theme"). Swapping
`.main` for `.dark`/`.light` variants of the *same* two brand colors (rather than inventing a
third color pair) keeps the dot mesh visually "the same sphere, different theme" while solving
the actual problem: `.main` alone has poor contrast against a dark background in dark mode and
against a light background in light mode; the `.dark`/`.light` swap keeps each mode's dots
legible against `background.default` for that mode.

**Alternatives considered**:
- *New, bespoke color pair per theme unrelated to `primary`/`secondary`* — rejected: spec FR-008
  explicitly requires colors "consistent with existing theme tokens," and inventing new colors
  not in `palette.ts` would itself violate that requirement.
- *Read `theme.palette.primary.main` directly via `useTheme()` inside `ReactiveSphere`* —
  rejected as the *only* mechanism: `ReactiveSphere` runs inside an R3F `<Canvas>` where MUI's
  theme context is available but re-rendering the whole R3F tree on every theme toggle is
  unnecessary; deriving colors in a plain function keyed by `mode` (already read via
  `useThemeStore`, the same store `SceneBackground`'s `StaticFallback` already reads) avoids a
  second context dependency inside the 3D tree.

## §3. Voice persona selection strategy

**Decision**: Two-tier client-side selection in `features/chat/voice/selectPersonaVoice.ts`:

1. **Curated tier** (`voicePersonaMap.ts`): a static `Record<LanguageCode, Record<BrowserEngine,
   string[]>>` of *ordered candidate voice names* known to sound like a young-adult female
   persona for that language on that engine (e.g. Chromium's `Google UK English Female` /
   `Microsoft Zira` for `en`; the first name present in `speechSynthesis.getVoices()` for that
   (language, engine) wins). Detailed in `contracts/voice-persona-mapping.md`.
2. **Heuristic fallback** (`selectPersonaVoice.ts`): when no curated name is present in the
   browser's actual voice list (uncurated combination, or the named voice isn't installed on
   this specific OS), score every voice whose `lang` matches the target language by: name
   containing a known female-leaning token list (e.g. `female`, common female given names
   surfaced by browsers — `Samantha`, `Karen`, `Zira`, `Susan`, …) → highest score; local
   (`voice.localService`) preferred as a tiebreaker (lower network-failure risk); otherwise the
   first language-matching voice, never an unrelated-language voice.

Browser engine is detected via `navigator.userAgentData?.brands` where available, falling back
to a `navigator.userAgent` substring check (Chromium vs Firefox vs WebKit/Safari) — read once
per session, not per utterance.

**Rationale**: Directly implements the spec's resolved clarification (hybrid: curated mapping
as primary source of truth, documented heuristic as fallback) and constitution §7's "sourcing
or configuring a voice that fits this persona before the language ships" for the curated tier,
while FR-004's "closest available consistent option" gets a concrete, testable, non-arbitrary
definition via the heuristic instead of staying an abstract requirement.

**Known constraint carried into tasks.md**: the *exact* voice names available differ by OS
version, not just browser engine (e.g. Windows Edge vs. Windows Firefox both expose SAPI
voices, but macOS Safari exposes AVSpeechSynthesis voices unrelated to Windows names) — the
curated map's entries must be validated against real installed voices during implementation
(a manual cross-browser/OS audit task), not assumed correct from documentation alone. This is
why the heuristic fallback exists as a safety net rather than requiring 100% curated coverage
before shipping.

**Alternatives considered**:
- *Heuristic-only, no curated list* — rejected in clarification: less reliable, and the
  constitution's "sourcing or configuring... before the language ships" language implies a
  deliberate per-language step, not a purely emergent runtime heuristic.
- *Curated-only, no heuristic fallback* — rejected in clarification: leaves FR-004's "closest
  available consistent option" undefined for any (language, platform) pair not yet curated,
  and blocks shipping until every combination is manually verified.
- *Server-rendered TTS pipeline (true persona voice, provider-hosted)* — rejected in
  clarification per spec Assumptions: explicitly deferred as a separate, larger-scope follow-up
  (this was ADR-0005's original "real fix," intentionally not reopened here).

## §4. Lucy portrait asset handling

**Decision**: Store the canonical portrait as a single optimized image under
`src/assets/branding/lucy-portrait.<ext>` (format chosen during implementation — WebP with the
existing Vite static-asset pipeline, no new build tooling), imported via a standard Vite
static import (`import lucyPortrait from '../../assets/branding/lucy-portrait.webp'`) and
re-exported from one `LucyPortrait.tsx` presentational component so every consumer (toggle
FAB, `AuthLayout`) shares one `<img>`/`alt`-text contract instead of importing the raw asset
path directly. `LucyPortrait` accepts a `variant` prop (`'toggle' | 'auth'`) purely for sizing/
crop `sx`, not different source files, and an `onError` handler swaps to a generic MUI avatar
icon fallback (FR-014) rather than a broken-image icon.

**Rationale**: A single shared component keeps the "same character, consistent framing"
requirement (User Story 3) enforceable in one place rather than duplicated per usage site, and
matches constitution §7's design-system-first rule (a new shared component used by ≥2 features
is justified — here it's used by chat, auth, and every pre-auth page).

**Alternatives considered**:
- *Import the raw image directly at each usage site* — rejected: duplicates alt-text and
  error-fallback logic across 6+ call sites, risking exactly the "one-off illustration" drift
  User Story 3 is meant to prevent.
- *SVG illustration instead of the provided raster portrait* — rejected: the user explicitly
  supplied a specific reference photo/portrait as "the Lucy image," not a request for a new
  illustrated mark (the existing `BrandMark` already covers the abstract-mark role).

## §5. Auth-page visual redesign direction

**Decision**: Keep the existing "drafting table" design language (`palette.ts`'s warm graphite/
vellum neutrals, "Pen" primary, "Redline" secondary, `BrandMark`'s compass-seal, `AuthLayout`'s
title-block/drafting-grid split) rather than replacing it with a generic SaaS template — the
redesign is a *refinement* pass (typography scale, spacing rhythm, visual hierarchy, the new
Lucy portrait as a warm focal point balancing the technical aesthetic) applied to
`AuthLayout.tsx` and its consuming pages, not a rebrand. Concretely: increase the title-block
panel's visual weight (larger type scale, tighter vertical rhythm, portrait placed to
counterbalance the existing `BrandMark`+wordmark block), and tidy the form panel's spacing/
button hierarchy — informed by the `frontend-design` and `ui-ux-pro-max` skills' guidance on
intentional typography/spacing rather than default MUI spacing.

**Rationale**: The spec's User Story 4 asks for the pages to feel "on par with the visual
quality of the redesigned AI workspace," not for a different brand — replacing an already-
deliberate, documented design language (see `AuthLayout.tsx`'s own comments) with a generic
template would contradict the existing brand-consistency goal User Story 3 is built around.

**Alternatives considered**:
- *Full visual rebrand (new palette, new layout pattern)* — rejected: out of scope per spec
  (FR-015 says "cohesive with the platform's current... design language," not a new one), and
  would create a second visual language to maintain alongside the workspace's existing one.
