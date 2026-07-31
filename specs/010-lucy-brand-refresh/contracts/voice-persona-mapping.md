# Contract: Voice Persona Mapping

Internal frontend contract between `voicePersonaMap.ts` (data), `selectPersonaVoice.ts`
(resolution logic), and `useTextToSpeech.ts` (consumer). Not a network API — this is the
frontend-internal "interface" this feature introduces, documented as a contract per the plan
template because other work (adding a language, adding a browser) depends on its shape
staying stable.

## Types

```ts
export type LanguageCode = 'en' | 'ar' | 'es' | 'fr' | 'de'
export type BrowserEngine = 'chromium' | 'firefox' | 'webkit'

/** Ordered candidate voice names for one (language, engine) pair — first match in
 * speechSynthesis.getVoices() wins. Absent key = not yet curated (use heuristic). */
export type VoicePersonaMap = Partial<
  Record<LanguageCode, Partial<Record<BrowserEngine, string[]>>>
>

export interface SelectedVoiceResult {
  voice: SpeechSynthesisVoice | null
  source: 'curated' | 'heuristic' | 'none'
}
```

## Function contracts

### `detectBrowserEngine(): BrowserEngine | 'unknown'`

- MUST be a pure read of `navigator` (no side effects), safe to call outside React.
- MUST return `'unknown'` rather than throwing when neither `userAgentData` nor `userAgent`
  yields a confident match — callers treat `'unknown'` the same as "no curated entry" and fall
  straight to the heuristic tier.

### `selectPersonaVoice(lang: LanguageCode, voices: SpeechSynthesisVoice[]): SelectedVoiceResult`

- **Preconditions**: `voices` is whatever `speechSynthesis.getVoices()` returned at call time
  (caller's responsibility to have waited for the `voiceschanged` event at least once — see
  Notes below); may legitimately be empty.
- **Postconditions**:
  - If a curated candidate name for `(lang, detectBrowserEngine())` exists in `voices`, return
    it with `source: 'curated'`.
  - Else, if at least one voice in `voices` has `lang` matching the target language (prefix
    match, e.g. `'en'` matches `'en-US'`), return the highest-scoring one per the heuristic
    (research.md §3) with `source: 'heuristic'`.
  - Else return `{ voice: null, source: 'none' }` — MUST NOT return an unrelated-language
    voice as a last resort (constitution §2.VIII / spec FR-004: never an arbitrary fallback).
- **MUST be a pure function** — no calls to `speechSynthesis` itself, no I/O — so it is
  unit-testable with hand-constructed `SpeechSynthesisVoice`-shaped fixtures without a real
  browser voice catalog (constitution §10: unit tests run without network/filesystem access;
  the DOM-dependent parts — `getVoices()`, engine sniffing — stay in thin call-site wrappers).

### `useTextToSpeech.speak(text, lang)` (modified contract)

- MUST call `selectPersonaVoice` (via `getVoices()` + `detectBrowserEngine()`) instead of the
  current `voices.find(v => v.lang.startsWith(lang))`.
- When `SelectedVoiceResult.voice` is `null`, MUST surface the existing user-visible error path
  (`setError(...)`) rather than calling `speechSynthesis.speak()` with no voice set (which
  today would silently fall back to the browser's own arbitrary default — the exact behavior
  this feature exists to eliminate).

## Notes

- **`getVoices()` timing**: Chromium/Firefox populate the voice list asynchronously; callers
  MUST have handled `speechSynthesis.onvoiceschanged` at least once before relying on a
  non-empty list (a pre-existing constraint of the Web Speech API, not new to this feature) —
  `useTextToSpeech.ts` already calls `getVoices()` lazily inside `speak()`, so this is a timing
  note for the manual cross-browser audit, not a new code path to add.
- This contract is intentionally internal/unversioned in the API sense (no REST/OpenAPI
  surface) — "contract" here means "the shape other tasks in tasks.md must not silently
  diverge from," per the plan template's guidance for non-networked applications.
