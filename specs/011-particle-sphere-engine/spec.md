# Feature Specification: Particle Sphere Rendering Engine Upgrade

**Feature Branch**: `011-particle-sphere-engine`

**Created**: 2026-08-02

**Status**: Draft

**Input**: User description: "The current implementation of the particle sphere does not match the intended design or behavior shown in the reference image. Review the existing particle sphere implementation, review an attached reference implementation, and compare both against the reference image to identify architectural and rendering differences. Refactor the existing implementation — preserving the project's architecture, coding standards, and component structure — so the assistant's presence renders as a uniform Fibonacci-distributed particle sphere with smooth glowing circular particles (custom shaders, not basic point materials), a soft neon appearance with additive blending, bloom post-processing, subtle breathing animation, slow continuous rotation, GPU-based animation, and performance suitable for 100k+ particles, organized as a modular, reusable particle engine with clean separation between geometry, shaders, animation, and post-processing. No parallel/duplicate implementation, no obsolete leftover code, full compatibility with the rest of the project."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A dot mesh that actually looks like the reference (Priority: P1)

A user opens the AI workspace and sees the assistant's presence rendered as a sphere made of many small, evenly-spread glowing dots — not dots bunched into visible rings or clustered near the poles, but spread uniformly across the whole sphere surface, each dot rendered as a soft-edged glowing point rather than a hard flat circle, with a subtle neon "halo" around the sphere as a whole.

**Why this priority**: This is the platform's signature visual element, visible on essentially every chat session. The current implementation was already shipped once (010-lucy-brand-refresh) and still doesn't match the intended look, so closing that gap is the core value of this feature — everything else (performance, code structure) supports this outcome.

**Independent Test**: Can be fully tested by opening the AI workspace and visually comparing the rendered sphere against the reference image — confirming uniform (non-ringed, non-clustered) dot spread and a soft glowing appearance rather than flat dots — without needing any of the other stories to be complete.

**Acceptance Scenarios**:

1. **Given** the AI workspace is open, **When** the assistant is idle, **Then** the sphere's dots are spread evenly across its whole surface (no visible banding into rings, no bunching at the poles).
2. **Given** the AI workspace is open, **When** the assistant is idle, **Then** each dot renders with a soft glowing edge and the sphere as a whole has a visible neon-style glow, rather than appearing as flat, hard-edged points.
3. **Given** two or more dots visually overlap from the camera's viewpoint, **When** they render, **Then** the overlapping area appears brighter than a single dot, not simply the top dot occluding the one behind it.

---

### User Story 2 - A calm, living presence (Priority: P2)

While the assistant is idle, the sphere should feel alive but calm: it slowly rotates and gently "breathes" (a subtle pulsing/scaling or density shift), independent of and in addition to the existing voice-reactive deformation that already happens while the assistant is speaking.

**Why this priority**: This refines the idle motion so the sphere doesn't feel static between the moments it's reacting to voice — it's a smaller behavioral addition layered on top of User Story 1's visual rework, so it can ship right after the rendering change lands.

**Independent Test**: Can be fully tested by watching the sphere while the assistant is silent and confirming it rotates continuously and exhibits a slow, repeating breathing-style pulse, then confirming the existing voice-reactive intensification (from 010-lucy-brand-refresh) still layers on top correctly when the assistant speaks.

**Acceptance Scenarios**:

1. **Given** the assistant is idle, **When** the user watches the sphere for several seconds, **Then** it rotates continuously at a slow, steady pace.
2. **Given** the assistant is idle, **When** the user watches the sphere for several seconds, **Then** it exhibits a subtle, repeating breathing-style pulse distinct from the rotation.
3. **Given** the assistant begins speaking, **When** voice-reactive deformation kicks in, **Then** it layers on top of the idle rotation/breathing without either animation visibly interrupting or replacing the other.

---

### User Story 3 - The richer visual holds up across devices (Priority: P3)

A user on capable desktop hardware sees the sphere rendered at a much higher particle density than before (dramatically smoother, more "dust-like" than the current sparse dot pattern), while a user on a lower-end device or with reduced-motion preferences set still gets a recognizable, on-brand version of the same sphere at a lower cost, exactly as the platform already guarantees today.

**Why this priority**: The richer look only delivers value if it doesn't break the platform's existing device-tiering and accessibility guarantees, but it's secondary to first getting the look itself right (User Story 1).

**Independent Test**: Can be fully tested by loading the workspace on both a high-end and a throttled/low-end device profile (or simulating one via the platform's existing quality-tier mechanism) and confirming the high-end profile renders a visibly denser, richer sphere while the low-end/reduced-motion profile still renders a recognizable sphere without dropping frame rate below the platform's existing acceptable threshold.

**Acceptance Scenarios**:

1. **Given** a user on capable desktop hardware, **When** the sphere renders, **Then** it displays a substantially higher particle count than the platform's current default, remaining smooth (no stutter).
2. **Given** a user on the platform's lower graphics-quality tier, **When** the sphere renders, **Then** it still renders as a recognizable, on-brand dot sphere using the reduced tier's simpler rendering technique (FR-010), not a blank space or an unrelated fallback shape.
3. **Given** a user with reduced-motion preferences enabled, **When** the sphere renders, **Then** idle rotation and breathing are frozen and reactive intensity stays capped, exactly as the platform's existing reduced-motion behavior already guarantees.
4. **Given** sustained frame-rate regression is detected on a "full" tier device, **When** the platform's existing performance monitor reacts, **Then** the sphere still steps down to the lower-density tier exactly as it does today.

---

## Clarifications

### Session 2026-08-02

- Q: Should the "full" tier's particle count be a fixed exact number, an adaptive/probed ceiling, or left directional with no number fixed by the spec? → A: Directional only — no specific number is fixed by this spec; SC-002 stays qualitative ("substantially higher than today, smooth, no sustained stutter"), and exact tuning is a planning/implementation decision.
- Q: Should the neon glow/bloom effect (FR-004) be scoped to just the particle sphere, or applied to the whole 3D scene canvas? → A: Sphere-only — the glow/bloom effect must affect only the particle sphere layer; other current or future scene elements (ambient light, OrbitControls, anything added later) must not be affected by it.
- Q: On the "reduced" quality tier, should the sphere keep the same glow/shader rendering technique as "full" (just fewer particles), or fall back to a visually simpler technique for performance? → A: Simpler fallback technique — the "reduced" tier deliberately uses a cheaper, visually simpler rendering approach (closer to today's flat, non-glowing dots) distinct from the "full" tier's glow/shader engine, prioritizing performance/battery life over full visual parity.

### Edge Cases

- What happens on a device where the higher particle count or glow effect causes sustained frame-rate regression? The platform's existing one-way performance downgrade (full → reduced) must still trigger and produce an acceptable result, not a frozen or crashed scene.
- What happens when the user toggles light/dark mode while the richer sphere is mid-animation? The dot color transition must complete without flicker or a mismatched-theme flash, exactly as today.
- What happens on a browser/device that supports WebGL2 (so the sphere renders at all) but not the specific rendering technique used for the glow effect? The scene's existing error boundary MUST catch the failure and fall back to the themed static placeholder, exactly as it would for any other scene-render failure — never a crash or a blank canvas.
- What happens to the existing automated tests and visual/behavioral guarantees from 010-lucy-brand-refresh (voice-reactive behavior, theme recoloring, reduced-motion, quality tiers, accessibility)? They must all continue to pass, updated only where they specifically assert on the old ring-pattern visual, never left broken.
- What happens to the previous ring-pattern rendering code and any assets tied only to it? It must be removed as part of this change, not left in the codebase alongside the new implementation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST render the assistant's presence as a particle sphere whose dots are uniformly distributed across the whole sphere surface (even pole-to-equator spread, no visible ring banding or polar clustering) — superseding 010-lucy-brand-refresh's FR-006 concentric/orbital-ring pattern requirement.
- **FR-002**: Each particle MUST render with a soft, glowing circular appearance (smooth falloff at the edges) rather than a flat, hard-edged shape.
- **FR-003**: Overlapping/densely-packed particles MUST visually brighten where they overlap, rather than the nearer particle simply hiding the ones behind it.
- **FR-004**: The sphere MUST display an overall soft neon-style glow, consistent with the reference image, in addition to each individual particle's own glow. This glow effect MUST be scoped to the sphere itself and MUST NOT visually affect other elements sharing the same 3D scene (e.g. ambient light, camera controls, or any element added to the scene in the future).
- **FR-005**: While idle, the sphere MUST continuously rotate at a slow, steady pace.
- **FR-006**: While idle, the sphere MUST exhibit a subtle, repeating "breathing" pulse distinct from and in addition to its rotation.
- **FR-007**: The existing voice-reactive behavior (dots intensifying/deforming while the assistant speaks, from 010-lucy-brand-refresh FR-007) MUST continue to function unchanged in when it triggers, layered on top of the idle rotation/breathing animation.
- **FR-008**: The existing per-theme dot color palette behavior (light/dark mode recoloring, from 010-lucy-brand-refresh FR-008) MUST continue to function unchanged with the new rendering approach.
- **FR-009**: The "full" quality tier MUST support a substantially higher particle count than the platform's current default, remaining smooth (no sustained stutter) on capable desktop hardware.
- **FR-010**: The existing "reduced" and "static-fallback" quality tiers MUST continue to exist and render a recognizable, on-brand version of the sphere, exactly as the platform already guarantees today. The "reduced" tier MUST deliberately use a simpler, cheaper rendering technique than the "full" tier's glow/shader engine (closer to today's flat, non-glowing dots) rather than the same technique at a lower particle count, prioritizing performance on constrained devices over full visual parity with "full."
- **FR-011**: The existing reduced-motion behavior (frozen idle animation, capped reactive intensity, from 010-lucy-brand-refresh FR-009) MUST continue to function unchanged, and MUST also freeze the new breathing animation.
- **FR-012**: The existing one-way performance-regression downgrade (from "full" to "reduced" on sustained frame-time regression) MUST continue to function with the new rendering approach.
- **FR-013**: The previous ring-pattern rendering implementation MUST be fully removed, not retained alongside the new implementation. This does not prohibit the "reduced" tier's intentionally simpler rendering technique (FR-010) — that is a deliberate, distinct code path, not leftover/dead code from the old implementation.
- **FR-014**: The updated visualization MUST continue to meet the platform's existing accessibility treatment for this element (exposed to assistive technology as decorative, consistent with its current behavior).

### Key Entities

- **Particle Sphere**: The assistant's visual presence — a sphere-shaped field of individually glowing particles with an idle state (rotation + breathing), a voice-reactive state, a per-theme color palette, and a per-quality-tier density/effect level.
- **Quality Tier**: The existing full / reduced / static-fallback classification (010-lucy-brand-refresh) that now also governs particle count and which rendering technique is used — the "full" tier's glow/shader engine versus the "reduced" tier's simpler, non-glowing technique (FR-010) — in addition to the density behavior it already controls.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a side-by-side visual comparison against the reference image, reviewers judge the rendered sphere's dot spread and glow appearance as matching (uniform spread, soft glow) rather than resembling the prior ring-patterned, flat-dot look.
- **SC-002**: On capable desktop hardware, the sphere renders at a materially higher particle count than the platform's current default while holding the platform's existing acceptable frame-rate threshold, with zero sustained stutter observed in testing.
- **SC-003**: 100% of 010-lucy-brand-refresh's existing sphere-related behaviors (voice-reactive intensification, theme recoloring, reduced-motion freezing, quality-tier degradation, performance-regression downgrade) still pass in a regression pass after this change, with zero functional regressions.
- **SC-004**: A code review of the sphere's rendering code finds zero leftover code from the old ring-pattern implementation; the "full" and "reduced" tiers' distinct rendering techniques (FR-010) are the only two intentional code paths, with no additional unused/duplicate variants left behind.
- **SC-005**: Users on the platform's lower-end quality tier still perceive the sphere as a recognizable, on-brand dot sphere (via its own simpler rendering technique, FR-010), not a blank area or a fallback shape unrelated to the "full" tier's appearance.

## Assumptions

- This feature supersedes 010-lucy-brand-refresh's FR-006 (concentric/orbital-ring dot pattern) with a uniform Fibonacci-style distribution, per the user-supplied reference image and reference implementation; 010's other sphere requirements (voice-reactive behavior, theme-based recoloring, reduced-motion/quality-tier compliance) remain in force and are carried forward as FR-007/008/010–012 above.
- "100k+ particles" (from the feature description) is illustrative context for the scale of "full"-tier improvement the user has in mind, not a numeric target fixed by this spec (see Clarifications) — no specific numeric floor/ceiling per tier is committed here beyond "substantially higher than today" and "still recognizable" (FR-009/SC-002); the "reduced" and "static-fallback" tiers keep materially lower particle counts consistent with their existing mobile/low-end purpose, with exact per-tier tuning left to the planning/implementation phase.
- The glow/neon effect is a static visual treatment (not itself a form of motion), so it is not gated by prefers-reduced-motion the way rotation/breathing/reactive intensity are; it may still be reduced or disabled on lower quality tiers purely for performance reasons.
- "Full compatibility with the rest of the project" and "modular, reusable particle engine architecture" mean the new implementation integrates within the existing scene/quality-tier structure and the platform's Clean Architecture/SOLID/KISS principles (see constitution.md), not that a separate rendering framework or new external dependency category is introduced; specific module boundaries are a planning-phase decision, not fixed here.
- The attached reference implementation (an AI-generated prototype HTML file) is treated purely as an algorithmic/technical reference for comparison, not as code to be merged in as-is — consistent with the user's explicit instruction.
- No new user-facing controls are introduced by this feature (no settings toggle for particle density, glow intensity, etc.) — all tuning is a fixed, per-quality-tier implementation decision, matching how the current sphere's parameters are handled today.
