import { describe, expect, it } from 'vitest'
import { catalogOf, leaves, namespaces } from './catalogTestUtils'
import { PROTECTED_TERMS, protectedTermsIn, termPattern } from './protectedTerms'
import { createT } from './useT'

describe('protected terms (R17, FR-046b, SC-016)', () => {
  it('is exactly the do-not-translate list', () => {
    expect([...PROTECTED_TERMS]).toEqual([
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
    ])
  })

  it('matches whole terms only', () => {
    expect(protectedTermsIn('Use the API key')).toEqual(['API'])
    expect(protectedTermsIn('RAPID and APIs')).toEqual([])
    expect(protectedTermsIn('Sign in to Ask Lucy with 2FA.')).toEqual(['Ask Lucy', '2FA'])
    expect(termPattern('PDF').test('a PDF file')).toBe(true)
  })

  for (const namespace of namespaces) {
    it(`keeps every protected term of an English ${namespace} string, verbatim, in its Arabic counterpart`, () => {
      const arabic = new Map(
        leaves(catalogOf('ar', namespace)).map((leaf) => [leaf.path, leaf.value]),
      )
      for (const { path, value } of leaves(catalogOf('en', namespace))) {
        const forms = typeof value === 'string' ? [value] : Object.values(value)
        for (const english of forms) {
          for (const term of protectedTermsIn(english)) {
            const counterpart = arabic.get(path)!
            const texts =
              typeof counterpart === 'string' ? [counterpart] : Object.values(counterpart)
            for (const text of texts)
              expect(text, `${namespace}.${path} must keep "${term}"`).toContain(term)
          }
        }
      }
    })
  }

  it('does not translate a protected term passed as a parameter', () => {
    const t = createT('notifications', 'ar')
    expect(t('preferences.switchLabel', { category: 'OpenAI', channel: 'SMTP' })).toContain(
      'OpenAI',
    )
    expect(t('preferences.switchLabel', { category: 'OpenAI', channel: 'SMTP' })).toContain('SMTP')
  })
})
