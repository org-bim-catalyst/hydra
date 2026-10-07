/**
 * Mirror of `docs/localization/do-not-translate.md` (R17, FR-046b): names and acronyms that stay exactly as
 * written, in every language, on every screen. Keep this list identical to the document and to the backend's
 * `ProtectedTerms.cs`; do not add or drop a term in only one place.
 */
export const PROTECTED_TERMS: readonly string[] = [
  'OpenAI',
  'Anthropic',
  'Gemini',
  'OpenRouter',
  'Ask Lucy',
  'API',
  'MCP',
  'SMTP',
  '2FA',
  'TOTP',
  'RAG',
  'OCR',
  'BIM',
  'PDF',
]

const escapeRegExp = (text: string) => text.replace(/[.*+?^${}()[\]\\]/g, '\\$&')

/** Whole-word, case-sensitive match so "API" is found in "the API key" but not inside "RAPID". */
export function termPattern(term: string): RegExp {
  return new RegExp(`(?<![\\p{L}\\p{N}])${escapeRegExp(term)}(?![\\p{L}\\p{N}])`, 'u')
}

/** The protected terms that appear in `text`. */
export function protectedTermsIn(text: string): string[] {
  return PROTECTED_TERMS.filter((term) => termPattern(term).test(text))
}
