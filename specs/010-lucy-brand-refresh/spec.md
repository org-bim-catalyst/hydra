# Feature Specification: Lucy Brand & Voice Refresh

**Feature Branch**: `010-lucy-brand-refresh`

**Created**: 2026-07-31

**Status**: Draft

**Input**: User description: "Change the animated hero sphere to a mesh-of-dots visualization (particle sphere made of many small dots forming concentric/orbital ring patterns, similar to the reference image) instead of the current solid/gradient sphere. The dot colors must adapt automatically when the user toggles light mode vs dark mode (distinct color palette per theme, consistent with existing theme tokens). Additionally, ensure text-to-speech (TTS) voice output always uses a consistent young-adult female voice persona across every supported browser and every supported language, rather than falling back to whatever default voice a given browser/OS/locale happens to expose. Also improve the overall visual design of the SaaS marketing/landing page (currently considered "ugly") using available design skills, and introduce the "Lucy" character image as a consistent branding element across the app: on the chat panel open/close toggle button, on the login page, and on other relevant pages."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A voice that always sounds like Lucy (Priority: P1)

A user turns on voice output while chatting with the assistant. Regardless of which browser they use, which operating system it runs on, or which language they've set the conversation to, the voice they hear always sounds like the same young-adult female persona — never a robotic default voice, a male voice, or a different-sounding voice depending on the device.

**Why this priority**: This closes a previously documented, deliberately deferred gap (the platform's own voice-persona requirement has been unmet since the last redesign). It affects every voice interaction on the platform today, not just new UI, and is the most visible break between the product's stated identity and what users actually hear.

**Independent Test**: Can be fully tested by triggering voice output across all three major browser engines (Chromium, Firefox, and WebKit/Safari — including their mobile equivalents) and at least two different supported languages, and confirming the perceived voice persona (pitch, tone, gender presentation) sounds consistent across all of them, with no run producing a noticeably different-sounding voice.

**Acceptance Scenarios**:

1. **Given** a user has voice output enabled and their conversation language set to English, **When** the assistant speaks a response, **Then** the voice heard is a young-adult female persona.
2. **Given** a user switches their conversation language to a different supported language, **When** the assistant speaks a response, **Then** the voice persona (perceived age, gender, tone) sounds like the same character as in English, not a different default voice for that language.
3. **Given** a user's browser or device has no installed voice that matches the required persona for their selected language, **When** the assistant attempts to speak, **Then** the system still produces speech using the closest available consistent persona rather than silently substituting an arbitrary, unrelated system voice.
4. **Given** voice output fails entirely on a given device (no synthesis capability at all), **When** the assistant attempts to speak, **Then** the user sees a visible error/notice rather than silence with no explanation.

---

### User Story 2 - A living mesh of light instead of a solid orb (Priority: P2)

A user opens the AI workspace and sees the assistant's presence represented not as a solid, gradient-shaded sphere, but as a sphere-shaped mesh made of many small glowing dots arranged in concentric, orbital ring patterns — similar in spirit to reference imagery of particle-sphere visualizations. The dots still idle gently and react while the assistant is speaking, just as the current sphere does. When the user switches between light mode and dark mode, the dots' colors change to a palette suited to that theme, rather than staying fixed.

**Why this priority**: This is the platform's signature visual element, visible on essentially every chat session, so it has high visibility — but it is a refinement of an existing, already-functional visualization (the sphere already animates and reacts to voice), so it carries lower risk than fixing an outright compliance gap.

**Independent Test**: Can be fully tested by opening the AI workspace, visually confirming the assistant presence renders as a dotted/particle mesh (not a solid shaded surface), toggling light/dark mode and confirming the dot colors change accordingly, and confirming the existing idle and voice-reactive animation behavior still functions.

**Acceptance Scenarios**:

1. **Given** the AI workspace is open, **When** the assistant is idle, **Then** the assistant's presence renders as a sphere-shaped arrangement of many discrete dots in concentric/orbital rings, not a solid shaded surface.
2. **Given** the workspace is in light mode, **When** the user switches to dark mode (or vice versa), **Then** the dot colors update to a palette appropriate to the new theme without requiring a page reload.
3. **Given** the assistant begins speaking, **When** voice output is active, **Then** the dot mesh reacts (as the current sphere does today) while preserving its dotted/mesh appearance rather than reverting to a solid surface.
4. **Given** a user has reduced-motion preferences enabled, **When** the dot mesh is displayed, **Then** idle animation is frozen and reactive intensity is capped, consistent with the platform's existing reduced-motion behavior.
5. **Given** a device falls into the platform's lower graphics-quality tier, **When** the dot mesh renders, **Then** it still renders as a recognizable dotted/mesh sphere (at reduced density/detail if necessary) rather than silently reverting to the old solid sphere or a blank space.

---

### User Story 3 - Lucy's face as a recognizable thread through the app (Priority: P3)

A user encounters the "Lucy" character portrait at the key moments they interact with the brand: when they open or close the AI chat panel, when they arrive at the login page, and at other prominent, brand-representing moments in the app (such as the registration/other auth pages). The portrait is used consistently — same character, consistent framing/treatment — so it reads as a single, recognizable identity rather than a one-off illustration.

**Why this priority**: Branding consistency reinforces product identity and trust, but it is additive polish that doesn't change core functionality, so it can ship independently of the sphere and voice work.

**Independent Test**: Can be fully tested by visiting the login page and confirming Lucy's portrait appears as part of the page's branding, then opening the chat workspace and confirming the chat panel's open/close toggle control displays Lucy's portrait, then visiting the other designated branded pages and confirming the same portrait treatment appears there too.

**Acceptance Scenarios**:

1. **Given** a signed-out user visits the login page, **When** the page loads, **Then** Lucy's portrait is visibly present as part of the page's branding.
2. **Given** a user is in the AI workspace, **When** they view the control that opens/closes the chat panel, **Then** that control displays Lucy's portrait rather than a generic icon.
3. **Given** a user visits another designated brand-facing page (e.g. registration), **When** the page loads, **Then** Lucy's portrait appears using the same visual treatment as on the login page.
4. **Given** Lucy's portrait is displayed anywhere in the app, **When** assistive technology encounters it, **Then** it is exposed with appropriate alternative text rather than being announced as a meaningless image.

---

### User Story 4 - A first impression that matches the product's ambition (Priority: P4)

A prospective or returning user's first-impression, public-facing pages (the sign-in/sign-up experience that today serves as the app's front door) look and feel like a polished, modern SaaS product — on par with the visual quality of the redesigned AI workspace itself — rather than a plain, dated form layout.

**Why this priority**: This is a visual-polish pass on already-functional pages; it improves perception and trust but changes no functional behavior, making it safe to sequence last.

**Independent Test**: Can be fully tested by loading the redesigned public page(s) and confirming they use a cohesive, modern visual design (typography, spacing, color, layout) consistent with the rest of the rebranded product, in both light and dark mode.

**Acceptance Scenarios**:

1. **Given** a signed-out user visits the app's public entry page(s), **When** the page loads, **Then** the layout, typography, and color treatment are visually consistent with the platform's current design language rather than the prior plain form layout.
2. **Given** the redesigned public page(s), **When** the user toggles light/dark mode, **Then** the page's visual design remains cohesive and legible in both themes.
3. **Given** the redesigned public page(s), **When** viewed on a narrow (mobile-width) viewport, **Then** the layout remains usable and visually coherent (no overlapping or cut-off content).

---

### Edge Cases

- What happens when a browser exposes zero speech-synthesis voices at all for the user's language (not just a persona mismatch)? System must still surface a clear, user-visible outcome (either best-effort speech in a fallback language/voice, or a visible error) — never silent failure.
- What happens when a user rapidly toggles light/dark mode while the dot mesh is mid-animation? The color transition must complete without visual glitching (flashing, wrong-theme colors persisting) and without breaking the idle/reactive animation loop.
- What happens on a very low-end device where even a reduced-density dot mesh is too costly to render? The platform's existing static-fallback tier must still produce a themed, on-brand placeholder rather than an error or blank area.
- What happens if the Lucy portrait asset fails to load (network failure, missing file)? The affected control/page must degrade gracefully (e.g. a generic icon/placeholder) rather than showing a broken-image icon or blocking the page.
- What happens to existing automated visual/accessibility tests that reference the current solid-sphere sphere or the current plain login layout? They must be updated to reflect the new visuals rather than left failing.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST select a text-to-speech voice matching a single defined persona (young-adult female) for every supported conversation language, rather than selecting whichever default voice the browser/OS happens to expose for that language.
- **FR-002**: System MUST produce a perceptually consistent voice persona (tone, pitch, apparent age/gender) across all three major browser engines — Chromium, Firefox, and WebKit/Safari, including their mobile equivalents (e.g. iOS Safari) — for the same language.
- **FR-003**: System MUST maintain a curated, versioned mapping of persona-matching voice(s) per supported (language, browser/platform) combination, used as the primary source of truth for voice selection.
- **FR-004**: When a given (language, browser/platform) combination has no curated mapping entry, system MUST fall back to a documented heuristic that scores the browser's available voices against persona criteria (e.g. name/gender/locale metadata) and selects the closest match, rather than picking an unscored/arbitrary default voice.
- **FR-005**: System MUST surface a visible, user-facing error when voice output cannot be produced at all — no curated voice, no heuristic match, and no synthesis capability (no silent failure) — consistent with the platform's existing error-handling requirements.
- **FR-006**: System MUST replace the AI workspace's current solid/gradient sphere visualization with a dot/particle-based mesh forming a sphere shape, arranged in concentric/orbital ring patterns.
- **FR-007**: The dot mesh MUST preserve existing idle animation and voice-reactive behavior (deforming/intensifying while the assistant speaks) that the current sphere provides today.
- **FR-008**: The dot mesh's color palette MUST automatically change when the user switches between light mode and dark mode, using colors consistent with the platform's existing theme tokens, without requiring a page reload.
- **FR-009**: The dot mesh MUST respect the platform's existing reduced-motion and reduced-graphics-quality behaviors (freezing/capping animation, reducing dot density) rather than bypassing them.
- **FR-010**: System MUST display the Lucy character portrait as part of the chat panel's open/close toggle control.
- **FR-011**: System MUST display the Lucy character portrait as part of the login page's branding.
- **FR-012**: System MUST display the Lucy character portrait, using the same visual treatment, on the registration page and other designated public/brand-facing auth pages.
- **FR-013**: Every use of the Lucy portrait MUST include appropriate alternative text for assistive technology.
- **FR-014**: If the Lucy portrait asset fails to load, the surrounding control or page MUST degrade to a graceful fallback rather than displaying a broken image or blocking page functionality.
- **FR-015**: System MUST redesign the visual presentation of the app's public-facing sign-in/sign-up pages so their layout, typography, and color treatment are cohesive with the platform's current (post-workspace-redesign) design language, in both light and dark mode.
- **FR-016**: The redesigned public-facing pages MUST remain fully usable and visually coherent at mobile-width viewports.
- **FR-017**: All updated visuals (dot mesh, Lucy branding, redesigned public pages) MUST meet the platform's existing accessibility standards (e.g. color contrast, keyboard operability of interactive controls).

### Key Entities

- **Voice Persona**: The defined "young-adult female" character voice that TTS output must consistently represent, independent of which underlying browser/OS voice engine is used to produce it. Backed by a curated, versioned mapping of persona-matching voices per (language, browser/platform) combination, with a documented scoring heuristic as fallback for combinations not yet curated.
- **Dot Mesh Sphere**: The particle-based visual representation of the assistant's presence, replacing the previous solid sphere; has an idle state, a voice-reactive state, and a per-theme color palette.
- **Lucy Portrait Asset**: The canonical character image used as a recognizable branding element across designated touchpoints (chat toggle, login, registration, other brand-facing pages).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a test pass across all three major browser engines (Chromium, Firefox, WebKit/Safari, including mobile equivalents) and at least two supported languages, 100% of voice-output runs are judged (by a consistent human rubric) to sound like the same persona — zero runs producing a noticeably different-sounding voice.
- **SC-002**: 100% of voice-output failures (no available synthesis) result in a visible user-facing notice, with zero silent (no feedback) failures observed in testing.
- **SC-003**: Users can visually distinguish the new dot-mesh assistant visualization from the prior solid sphere in under 1 second of viewing (i.e., the change is unambiguous, not a subtle tweak).
- **SC-004**: Toggling light/dark mode updates the dot mesh's colors with no visually perceptible delay or flash of incorrect-theme color.
- **SC-005**: Lucy's portrait is present and visually consistent across 100% of the designated touchpoints (chat toggle, login, registration, other designated brand pages) in a full click-through audit.
- **SC-006**: In an informal before/after comparison, the redesigned public-facing pages are rated as visually consistent with the rest of the rebranded product by reviewers, with no reviewer identifying the public pages as looking like a different, older product.
- **SC-007**: The redesigned public pages and Lucy branding pass the platform's existing automated accessibility checks with zero new violations introduced.

## Assumptions

- "Every supported browser" means all three major browser engines — Chromium, Firefox, and WebKit/Safari — including their mobile equivalents (e.g. iOS Safari); browsers with no speech-synthesis API support at all are already out of scope per existing platform behavior (FR-005 still requires a visible error in that case). See Clarifications.
- "Every supported language" means the platform's current conversation-language set: English, Arabic, Spanish, French, and German (the languages exposed today by the conversation language selector that also drives TTS output) — not an open-ended list of all human languages.
- The attached reference portrait image is treated as the canonical "Lucy" character likeness; production-ready cropped/sized variants will be derived from it during implementation.
- "Other relevant pages" for Lucy branding (User Story 3 / FR-011) is scoped to the registration page and the other existing pre-authentication auth pages (e.g. confirm-email, two-factor verification, external-login-complete) that share the same layout shell as the login page — not every authenticated in-app page.
- The dot-mesh sphere replaces the sphere's rendering technique only; it does not change where or when the sphere appears in the workspace, its size, or its role in the layout.
- Existing automated tests that assert on the current solid-sphere shader/material or the current plain login layout are expected to be updated as part of this feature's implementation, not treated as a regression.

## Clarifications

### Session 2026-07-31

- Q: Should the text-to-speech fix stay client-side (select/tune the best-matching browser voice per language) or fully migrate to a server-rendered audio pipeline (as floated, but explicitly deferred, in the prior redesign's ADR-0005)? → A: Client-side voice selection/tuning only; a server-rendered pipeline remains a separate, unscoped follow-up.
- Q: Does "SaaS page" refer to redesigning the existing public sign-in/sign-up pages, or building a new marketing/landing page (the app currently has none — `/` redirects straight to `/chat`)? → A: Redesign the existing public sign-in/sign-up pages (login, registration, and the auth pages sharing their layout shell); no new marketing/landing page is in scope.
- Q: For the voice-persona requirement, which browsers count as "every supported browser" (no existing documented browser matrix to inherit from)? → A: Chromium + Firefox + WebKit/Safari, including mobile equivalents (e.g. iOS Safari) — full modern-browser coverage.
- Q: How should the system pick a persona-matching voice per (language, browser/platform) combination? → A: Hybrid — a curated, versioned voice mapping as the primary source of truth, with a documented runtime heuristic (scoring available voices against persona criteria) as fallback for combinations not yet curated.
