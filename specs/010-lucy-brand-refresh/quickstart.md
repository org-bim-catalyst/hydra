# Quickstart: Validating Lucy Brand & Voice Refresh

Manual + automated validation for the four user stories. Run from
`src/AskLucy.Web/ClientApp`.

## Prerequisites

- Frontend dependencies installed (`npm install`, if not already).
- For the voice-persona checks (User Story 1): access to real Chromium, Firefox, and
  WebKit/Safari browsers — desktop, plus at least one mobile WebKit target (iOS Safari or
  Simulator) — since `speechSynthesis` voice catalogs cannot be simulated in `jsdom`/CI.

## Setup

```bash
npm run dev
```

Open the printed local URL.

## User Story 1 — Voice persona consistency

1. Sign in, open the chat panel, enable voice output, send a message that produces an
   assistant reply.
2. In each of Chrome/Edge (Chromium), Firefox, and Safari (WebKit/desktop or iOS): confirm the
   spoken voice sounds like the same young-adult female persona (per spec SC-001 — human
   judgment call, not automatable).
3. Switch the conversation language selector through at least English + two others (e.g.
   Arabic, Spanish) and repeat — confirm the persona still sounds consistent, not a different
   default voice per language.
4. **Fallback path**: on a browser/OS combination with no curated entry for the selected
   language, confirm speech still plays via the heuristic fallback (`source: 'heuristic'` in
   `selectPersonaVoice`'s result — inspectable via a temporary `console.log` or debugger during
   manual QA, not a production log) rather than silently using an unrelated voice.
5. **Error path**: on a browser with no `speechSynthesis` support at all (or by stubbing
   `window.speechSynthesis` to `undefined` in devtools), confirm a visible error message
   appears when voice output is attempted (FR-005/SC-002).
6. Automated: `npm test -- useTextToSpeech` and `npm test -- selectPersonaVoice` — unit tests
   cover the selection/heuristic logic with fixture voice lists (contracts/voice-persona-mapping.md).

## User Story 2 — Dot-mesh sphere

1. Open the chat workspace on a desktop browser with WebGL2 support; confirm the assistant
   presence renders as a sphere-shaped arrangement of discrete dots in concentric/orbital
   rings, not a solid shaded surface.
2. Toggle the app's light/dark mode control; confirm the dot colors switch immediately (no
   flash of the wrong theme's colors, no reload required) — SC-004.
3. Trigger an assistant voice reply; confirm the dot mesh still deforms/intensifies while
   speaking, same as the prior solid sphere did.
4. Enable OS-level "reduce motion"; confirm idle animation freezes and reactive intensity is
   capped, per the existing `reducedMotion` behavior.
5. Resize the browser below the mobile breakpoint (or use device emulation) to force the
   'reduced' quality tier; confirm a recognizable dotted/mesh sphere still renders (reduced
   density is acceptable, disappearing is not).
6. Force the 'static-fallback' tier (disable WebGL2, e.g. via `chrome://flags` or a throttled
   devtools GPU override) and confirm the themed static placeholder still renders — unchanged
   behavior from today, just verify it wasn't broken by the shader changes.
7. Automated: `npm test -- ReactiveSphere` / `npm test -- dotMeshTheme` and re-run the existing
   `ChatPage.a11y.test.tsx` suite to confirm no new a11y violations.

## User Story 3 — Lucy portrait branding

1. Sign out; visit `/login` — confirm Lucy's portrait is visibly part of the page branding.
2. Visit `/register` and the other pre-auth pages (`/confirm-email`, `/confirm-email-change`,
   `/auth/external-complete`) — confirm the same portrait treatment appears.
3. Sign in, open the chat workspace; confirm the chat panel's open/close toggle button displays
   Lucy's portrait (not the current chat/close icon alone).
4. Using a screen reader (or browser accessibility inspector), confirm every portrait instance
   exposes meaningful alt text, not an empty/generic label.
5. Simulate an asset load failure (block the image URL via devtools network throttling/block
   request) and confirm the affected control/page falls back gracefully rather than showing a
   broken-image icon.
6. Automated: `npm test -- AssistantToggleFab` and `npm test -- LoginPage` (or equivalent
   updated test files) plus the relevant `*.a11y.test.tsx` suites.

## User Story 4 — Auth page visual redesign

1. Visit `/login` and `/register`; confirm the layout, typography, and color treatment read as
   a deliberate, polished design (not the prior plain form layout) while still matching the
   "drafting table" brand language used elsewhere.
2. Toggle light/dark mode on these pages; confirm both remain legible and cohesive.
3. Resize to a mobile-width viewport; confirm the layout stays usable (no overlap/cut-off).
4. Automated: re-run `npm test` for any updated auth-page component/a11y tests; run
   `npm run lint` to confirm no new violations.

## Full regression pass

```bash
npm test
npm run lint
npm run build
```

All three MUST pass before considering the feature done (constitution §12 CI/CD gate 1–2
apply locally as a pre-check).
