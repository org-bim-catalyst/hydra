/**
 * specs/078 FR-005b — what Lucy shows and speaks when a mid-clip dictation failure means she
 * missed what the user said. Shown as a visible message in every case, and spoken aloud in Lucy's
 * own voice persona when voice replies are on (CLAUDE.md, User Experience) — never a vendor detail,
 * matching every other user-facing dictation notice.
 *
 * Mirrors `languageOptions.ts`'s `OFFER_VOICE_CUES`/`offerVoiceCue`: a language with no entry falls
 * back to English rather than to silence.
 */
export const GENTLE_REPEAT_BY_LANGUAGE: Record<string, string> = {
  en: 'Sorry, I missed that — could you say it again?',
  ar: 'عذرًا، لم أسمع ذلك — هل يمكنك تكراره؟',
  es: 'Lo siento, no entendí eso — ¿puedes repetirlo?',
  fr: "Désolé, je n'ai pas compris — pouvez-vous répéter ?",
  de: 'Entschuldigung, das habe ich nicht verstanden — kannst du das wiederholen?',
}

/** Matches on the base subtag, so `en-GB` and `ar-AE` resolve rather than falling through. */
export function gentleRepeatMessage(language?: string): string {
  const base = (language ?? 'en').split('-')[0].toLowerCase()
  return GENTLE_REPEAT_BY_LANGUAGE[base] ?? GENTLE_REPEAT_BY_LANGUAGE.en
}
