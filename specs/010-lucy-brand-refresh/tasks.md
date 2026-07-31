---

description: "Task list for Lucy Brand & Voice Refresh"
---

# Tasks: Lucy Brand & Voice Refresh

**Input**: Design documents from `/specs/010-lucy-brand-refresh/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/voice-persona-mapping.md](./contracts/voice-persona-mapping.md), [quickstart.md](./quickstart.md)

**Tests**: Included — constitution §10/§18 requires tests for new/changed behavior in the same PR that introduces it; this is not optional for this repository.

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P4) so each can be implemented, tested, and shipped independently. All paths are relative to `src/AskLucy.Web/ClientApp/src/` unless stated otherwise.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 (voice persona), US2 (dot-mesh sphere), US3 (Lucy portrait branding), US4 (auth page redesign)

---

## Phase 1: Setup

**Purpose**: Get the one new binary asset this feature needs into the repo before any story references it.

- [x] T001 Add the canonical Lucy portrait asset (derived from the user-supplied reference portrait) as a web image at `src/assets/branding/lucy-portrait.png`, reused at both toggle-button and auth-page scale (research.md §4) — shipped as PNG rather than WebP; no image-optimization tooling was available in this environment to re-encode it, and 339KB is acceptable for one bundled, browser-cached brand asset

**Checkpoint**: Asset exists and is importable via Vite's static asset pipeline.

---

## Phase 2: Foundational

**Purpose**: Blocking prerequisites shared by *all* user stories.

**None** — the four stories touch disjoint code areas (voice, 3D scene, a new shared branding component, and auth-page layout) with no common infrastructure to stand up first, per plan.md's Constitution Check (§2.III Simplicity/YAGNI: no infrastructure is introduced that isn't required by a specific story). Proceed directly to Phase 3. (US4 has one intra-feature dependency on US3, noted in Dependencies below — that is a story-to-story dependency, not a foundational blocker.)

---

## Phase 3: User Story 1 — A voice that always sounds like Lucy (Priority: P1) 🎯 MVP

**Goal**: TTS output uses a consistent young-adult female persona across Chromium, Firefox, and WebKit/Safari (incl. mobile) for all 5 supported languages, with a defined (never arbitrary) fallback and a visible error when speech truly cannot be produced.

**Independent Test**: Per spec.md — trigger voice output in each of the three browser engines across at least two supported languages and confirm the perceived persona is consistent; see quickstart.md "User Story 1."

### Tests for User Story 1

> Write these first; they must fail (or not compile) before the corresponding implementation task.

- [x] T002 [P] [US1] Unit tests for `detectBrowserEngine()` (Chromium/Firefox/WebKit detection + `'unknown'` fallback) in `features/chat/voice/detectBrowserEngine.test.ts`
- [x] T003 [P] [US1] Unit tests for `selectPersonaVoice()` covering curated-hit, heuristic-fallback, and no-match-returns-null cases using hand-built `SpeechSynthesisVoice`-shaped fixtures, per contracts/voice-persona-mapping.md, in `features/chat/voice/selectPersonaVoice.test.ts`
- [x] T004 [US1] Extend `features/chat/voice/useTextToSpeech.test.ts` with cases for: a curated voice being applied to the utterance, and `speak()` surfacing the existing visible error (`setError`) when `selectPersonaVoice` returns `{ voice: null }` instead of calling `speechSynthesis.speak()` with no voice set

### Implementation for User Story 1

- [x] T005 [P] [US1] Define `LanguageCode`, `BrowserEngine`, and `VoicePersonaMap` types and the curated voice-name data (best-effort initial entries, refined in T009) in `features/chat/voice/voicePersonaMap.ts`, per contracts/voice-persona-mapping.md
- [x] T006 [P] [US1] Implement `detectBrowserEngine()` in `features/chat/voice/detectBrowserEngine.ts` (makes T002 pass)
- [x] T007 [US1] Implement `selectPersonaVoice(lang, voices)` as a pure function combining the curated lookup (T005) with the scored heuristic fallback described in research.md §3, in `features/chat/voice/selectPersonaVoice.ts` (depends on T005, T006; makes T003 pass)
- [x] T008 [US1] Wire `selectPersonaVoice` into `features/chat/voice/useTextToSpeech.ts`'s `speak()`, replacing `voices.find(v => v.lang.startsWith(lang))`, and route a `null` result through the existing `setError(...)` path instead of calling `speechSynthesis.speak()` unset (depends on T007; makes T004 pass)
- [ ] T009 [US1] Manual cross-browser/OS voice audit — Chromium, Firefox, and WebKit/Safari (desktop) plus iOS Safari — across all 5 supported languages (en, ar, es, fr, de); update `voicePersonaMap.ts`'s curated entries with real, verified voice names found during the audit (research.md §3's documented constraint that exact names must be confirmed, not assumed)
- [x] T010 [US1] Add a short resolution note to `docs/adr/0005-defer-tts-voice-persona-fix.md` recording that this feature closes the persona-consistency gap the ADR deferred (constitution §13)

**Checkpoint**: User Story 1 is independently functional and testable — voice output sounds consistent across browsers/languages, with defined fallback and error behavior.

---

## Phase 4: User Story 2 — A living mesh of light instead of a solid orb (Priority: P2)

**Goal**: The workspace's assistant-presence sphere renders as a theme-reactive dot/particle mesh in concentric rings instead of a solid shaded surface, preserving existing idle/reactive/reduced-motion/quality-tier behavior.

**Independent Test**: Per spec.md — open the workspace, confirm the dotted mesh appearance, toggle light/dark mode and confirm color changes, confirm voice-reactive animation still works; see quickstart.md "User Story 2."

### Tests for User Story 2

- [x] T011 [P] [US2] Unit tests for `getDotMeshColors(mode)` covering light-mode and dark-mode outputs against the documented `palette.ts` token mapping (research.md §2) in `features/chat/scene/dotMeshTheme.test.ts`

### Implementation for User Story 2

- [x] T012 [P] [US2] Rewrite `features/chat/scene/sphere.vert.glsl` to displace per-point positions (Fibonacci-lattice sphere sampling grouped into latitude rings, research.md §1) using the existing simplex-noise/`uTime`/`uAmplitude`/`uFrequency` contract, and set `gl_PointSize` attenuated by camera distance
- [x] T013 [P] [US2] Rewrite `features/chat/scene/sphere.frag.glsl` to draw a soft circular point-sprite (discard outside a UV-space radius) shaded by the existing idle/reactive color mix, replacing the current Lambertian surface shading
- [x] T014 [P] [US2] Implement `getDotMeshColors(mode)` in `features/chat/scene/dotMeshTheme.ts` per data-model.md's `DotMeshThemeColors` (derives from `theme/tokens/palette.ts`'s `primary`/`secondary` `.light`/`.dark`, no new colors; makes T011 pass)
- [x] T015 [US2] Update `features/chat/scene/ReactiveSphere.tsx` to render `<points><bufferGeometry>` (Fibonacci-lattice point positions as a buffer attribute) instead of `<mesh><icosahedronGeometry>`, read the current theme mode via `useThemeStore`, and write `getDotMeshColors(mode)`'s output into the existing `uColorIdle`/`uColorReactive` uniforms on mode change (depends on T012, T013, T014)
- [x] T016 [US2] Verify the `qualityTier`-driven detail/density parameter (currently `detail = qualityTier === 'full' ? 4 : 2'`) still produces a recognizable, reduced-count dot lattice on the `'reduced'` tier, and that `SceneBackground.tsx`'s `StaticFallback` (already theme-aware) needs no changes — adjust `ReactiveSphere.tsx`'s point-count-by-tier logic if the reduced tier is too sparse or too dense
- [x] T016a [US2] Verify FR-009/Acceptance Scenario 4 explicitly: with `reducedMotion` true, confirm the rewritten `ReactiveSphere.tsx` still freezes idle rotation/animation and caps reactive amplitude (`uAmplitude`/`uFrequency` held at idle values) exactly as it did before the geometry rewrite — this behavior is inherited from the unchanged `useFrame`/`reducedMotion` branch, but must be manually re-confirmed against the new point-based geometry, not assumed

**Checkpoint**: User Stories 1 AND 2 both work independently; the sphere is now a theme-reactive dot mesh with unchanged animation behavior.

---

## Phase 5: User Story 3 — Lucy's face as a recognizable thread through the app (Priority: P3)

**Goal**: A single, reusable Lucy portrait appears with consistent treatment on the chat panel's open/close toggle and across the login/registration/other pre-auth pages, with required alt text and a graceful load-failure fallback.

**Independent Test**: Per spec.md — visit the login page and confirm the portrait, open/close the chat panel and confirm the toggle shows it, visit the other designated pages and confirm the same treatment; see quickstart.md "User Story 3."

### Tests for User Story 3

- [x] T017 [P] [US3] Component tests for `LucyPortrait` covering: `alt` is required and rendered on the `<img>`, and `onError` swaps to the fallback avatar/icon rather than leaving a broken image, in `features/chat/branding/LucyPortrait.test.tsx`
- [x] T018 [US3] Extend `features/chat/components/AssistantToggleFab.test.tsx` to assert the toggle renders `LucyPortrait` with appropriate `alt` text alongside the existing open/close behavior assertions
- [x] T019 [P] [US3] Add `features/auth/pages/LoginPage.a11y.test.tsx` (new — no auth-page tests exist yet) asserting zero `jest-axe` violations and that the portrait's alt text is present, following the pattern in `features/chat/pages/ChatPage.a11y.test.tsx`

### Implementation for User Story 3

- [x] T020 [P] [US3] Create the shared `LucyPortrait` component (`variant: 'toggle' | 'auth'`, required `alt` prop, `onError` fallback to a generic MUI avatar/icon) in `features/chat/branding/LucyPortrait.tsx`, importing the asset added in T001, per data-model.md's `LucyPortraitAsset` (makes T017 pass)
- [x] T021 [US3] Integrate `LucyPortrait` (`variant="toggle"`) into `features/chat/components/AssistantToggleFab.tsx` per FR-010 (depends on T020; makes T018 pass)
- [x] T022 [US3] Integrate `LucyPortrait` (`variant="auth"`) into `components/AuthLayout.tsx`'s title-block panel per FR-011/FR-012 — this automatically covers every page that renders through `AuthLayout` (`LoginPage`, `RegisterPage`, `ConfirmEmailPage`, `ConfirmEmailChangePage`, `ExternalLoginCompletePage`) (depends on T020; makes T019 pass)

**Checkpoint**: User Stories 1, 2, AND 3 all work independently; Lucy's portrait now appears consistently on the toggle and every pre-auth page.

---

## Phase 6: User Story 4 — A first impression that matches the product's ambition (Priority: P4)

**Goal**: The public sign-in/sign-up pages read as a polished, modern, on-brand experience (refining the existing "drafting table" language, not replacing it) in both themes and at mobile widths.

**Independent Test**: Per spec.md — load the redesigned pages, confirm cohesive typography/spacing/color consistent with the rest of the product, toggle light/dark, check mobile width; see quickstart.md "User Story 4."

**Depends on**: Phase 5 (US3) — this phase edits `AuthLayout.tsx` again, on top of the portrait integration from T022, rather than in parallel with it.

### Tests for User Story 4

- [x] T023 [P] [US4] Add `features/auth/pages/RegisterPage.a11y.test.tsx` asserting zero `jest-axe` violations after the redesign, following the same pattern as T019

### Implementation for User Story 4

- [x] T024 [US4] Refine `components/AuthLayout.tsx`'s typography scale, spacing rhythm, and title-block/form-panel visual hierarchy within the existing "drafting table" design language (graphite/vellum neutrals, Pen/Redline accents, `BrandMark`, drafting-grid texture) per research.md §5 — builds on T022's portrait placement rather than reverting it
- [x] T025 [P] [US4] Polish `features/auth/pages/LoginPage.tsx`'s form spacing and button/link hierarchy (visual only — no behavior change)
- [x] T026 [P] [US4] Polish `features/auth/pages/RegisterPage.tsx`'s form spacing and button/link hierarchy (visual only — no behavior change; makes T023 pass)
- [x] T027 [US4] Manually verify the redesigned pages at mobile width per quickstart.md "User Story 4" step 3; adjust `AuthLayout.tsx` responsive breakpoints/spacing if content overlaps or is cut off

**Checkpoint**: All four user stories are independently functional. Feature is complete per spec.md.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature verification that spans all four stories.

- [ ] T028 Run the full `quickstart.md` validation pass (all four user-story sections) end-to-end in a real browser
- [x] T029 Run `npm test`, `npm run lint`, and `npm run build` in `src/AskLucy.Web/ClientApp` and fix any failures (constitution §12 CI/CD gates 1–3, checked locally before PR)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Empty for this feature — proceeds straight through.
- **User Story 1 (Phase 3)**: Depends only on Setup being irrelevant to it (no dependency on T001). Fully independent.
- **User Story 2 (Phase 4)**: Fully independent of US1 and US3/US4 — different files entirely.
- **User Story 3 (Phase 5)**: Depends on T001 (the portrait asset). Independent of US1/US2.
- **User Story 4 (Phase 6)**: Depends on User Story 3 (Phase 5) being complete — both edit `components/AuthLayout.tsx`, and US4's polish is layered on top of US3's portrait placement, not done in parallel with it.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### User Story Dependencies

- **US1 (P1)**: No dependencies on other stories.
- **US2 (P2)**: No dependencies on other stories.
- **US3 (P3)**: No dependencies on other stories (only on Setup/T001).
- **US4 (P4)**: Depends on US3 (shared file: `AuthLayout.tsx`).

### Parallel Opportunities

- T001 (Setup) can run alongside early US1/US2 work since neither depends on it.
- US1, US2, and US3 can be implemented in parallel by different people/sessions once Setup is done — they touch entirely disjoint files.
- Within US1: T002/T003 (tests) in parallel; T005/T006 (types+data, engine detection) in parallel.
- Within US2: T011 (test) in parallel with T012/T013 (shaders, different files) and T014 (color function, different file).
- Within US3: T017/T019 (tests, different files) in parallel; T020 must land before T021/T022 but those two can then proceed in parallel (different files: `AssistantToggleFab.tsx` vs `AuthLayout.tsx`).
- Within US4: T025/T026 (different page files) in parallel; T023 in parallel with either.
- US4 must wait for US3 to finish (see above) — not parallelizable with it.

---

## Parallel Example: User Story 1

```bash
# Tests, launched together (different files):
Task: "Unit tests for detectBrowserEngine() in features/chat/voice/detectBrowserEngine.test.ts"
Task: "Unit tests for selectPersonaVoice() in features/chat/voice/selectPersonaVoice.test.ts"

# Implementation, launched together once tests exist (different files, no interdependency):
Task: "Define VoicePersonaMap types + curated data in features/chat/voice/voicePersonaMap.ts"
Task: "Implement detectBrowserEngine() in features/chat/voice/detectBrowserEngine.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001 — not actually required for US1, but cheap to do first).
2. Phase 2: Foundational — nothing to do, skip straight through.
3. Complete Phase 3: User Story 1 (T002–T010).
4. **STOP and VALIDATE**: run quickstart.md's User Story 1 section across all three browser engines.
5. This alone closes the ADR-0005 constitution gap and is deployable independently of the other three stories.

### Incremental Delivery

1. Setup → (no Foundational work) → ready.
2. Add User Story 1 → validate → ship (closes the constitution violation — highest-value increment).
3. Add User Story 2 → validate → ship (new signature visual, zero risk to US1).
4. Add User Story 3 → validate → ship (portrait branding).
5. Add User Story 4 → validate → ship (auth-page polish, layered on US3).

### Parallel Team Strategy

With multiple people:

1. One person takes Setup (T001) — quick, unblocks US3.
2. Once done: Person A → US1, Person B → US2, Person C → US3 — all independent.
3. Person C (or whoever finishes US3 first) proceeds into US4 once US3 lands, since US4 depends on it.
4. Everyone converges on Phase 7 (Polish) once their story is checkpointed.

---

## Notes

- [P] tasks touch different files with no unfinished dependency between them.
- [Story] labels map every implementation/test task back to spec.md's US1–US4 for traceability.
- Tests are included per constitution §10/§18 — write each story's tests before its implementation tasks and confirm they fail first.
- No task in this list touches `src/AskLucy.Web` (the .NET backend) — this feature is frontend-only (plan.md Constitution Check).
- T009 (voice audit), T016a (reduced-motion re-check on the new geometry), and T027 (mobile-width check) are manual verification tasks, not automatable in this stack (per quickstart.md) — track them as done only after the actual manual check, not just the code change.
