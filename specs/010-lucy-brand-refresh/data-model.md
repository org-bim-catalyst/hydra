# Phase 1 Data Model: Lucy Brand & Voice Refresh

This feature makes no backend/database changes (Constitution Check: N/A for §3/§5). "Entities"
below are frontend-only TypeScript data shapes — config modules and derived view state, not
persisted records.

## VoicePersonaMap

Curated source of truth for FR-003. Lives in `features/chat/voice/voicePersonaMap.ts`.

| Field | Type | Notes |
|---|---|---|
| `[languageCode]` | `LanguageCode` (`'en' \| 'ar' \| 'es' \| 'fr' \| 'de'`) | Matches `LanguageSelector.tsx`'s existing `LANGUAGES` codes — no new language codes introduced. |
| `[browserEngine]` | `BrowserEngine` (`'chromium' \| 'firefox' \| 'webkit'`) | Detected once per session (research.md §3); mobile equivalents map to the same engine as their desktop counterpart (e.g. iOS Safari → `webkit`). |
| candidate voice names | `string[]` | Ordered list; first name found in `speechSynthesis.getVoices()` for that engine wins. Empty/absent entry means "not yet curated — use heuristic fallback" (FR-004). |

**Validation rule**: every entry's candidate list MUST contain at least one name once curated
(an empty array is only valid as "not yet curated," represented by the key being absent, not
by an empty array — avoids an ambiguous empty-vs-uncurated state).

**Lifecycle**: static, versioned in source control (constitution §4: no runtime-configurable
magic values without a named constant). Adding a new supported language or engine is a code
change to this module plus a manual voice audit (research.md §3), not a data migration.

## SelectedVoiceResult

Return shape of `selectPersonaVoice()` (`features/chat/voice/selectPersonaVoice.ts`), consumed
by `useTextToSpeech.ts`.

| Field | Type | Notes |
|---|---|---|
| `voice` | `SpeechSynthesisVoice \| null` | `null` only when zero voices exist for the target language at all (edge case in spec.md) — triggers FR-005's visible error path, never a silent `undefined` voice. |
| `source` | `'curated' \| 'heuristic' \| 'none'` | Diagnostic only (not user-facing); lets the manual cross-browser audit (research.md §3) and any future logging distinguish which tier resolved the voice. |

## DotMeshThemeColors

Output of `getDotMeshColors(mode)` (`features/chat/scene/dotMeshTheme.ts`), consumed by
`ReactiveSphere.tsx`'s shader uniforms.

| Field | Type | Notes |
|---|---|---|
| `idle` | `string` (hex) | `theme.palette.primary.dark` (light mode) / `.light` (dark mode) — see research.md §2. |
| `reactive` | `string` (hex) | `theme.palette.secondary.dark` (light mode) / `.light` (dark mode). |

**State transition**: recomputed synchronously whenever `useThemeStore`'s `mode` changes;
`ReactiveSphere` writes the new values directly into the existing `uColorIdle`/`uColorReactive`
uniforms (no material/geometry recreation) — satisfies SC-004 (no perceptible delay/flash).

## LucyPortraitAsset

Not a data structure so much as a single static asset + its presentation contract, expressed
as `LucyPortrait`'s props (`features/chat/branding/LucyPortrait.tsx` or `components/`, exact
path decided in tasks.md).

| Field | Type | Notes |
|---|---|---|
| `variant` | `'toggle' \| 'auth'` | Controls sizing/crop `sx` only — same source image (research.md §4). |
| `alt` | `string` (required prop, no default) | Forces every call site to supply context-appropriate alt text (FR-013) — e.g. "Lucy" on the toggle, "Ask Lucy" on auth pages; no silent fallback to an empty/generic alt. |

**Failure mode**: `onError` on the underlying `<img>` swaps to a generic MUI `Avatar`/icon
placeholder in place, per FR-014 and the spec's edge case for a failed asset load — never a
broken-image icon, never a blocked page.
