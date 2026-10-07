import { describe, expect, it } from 'vitest'
import { catalogOf, leaves, namespaces, textsOf } from './catalogTestUtils'
import { PROTECTED_TERMS } from './protectedTerms'

/**
 * Arabic strings that may legitimately read the same as English: nothing but protected terms, placeholders,
 * punctuation or digits. A new entry needs a reason; "I have not translated it yet" is not one.
 */
const IDENTICAL_ALLOW_LIST: readonly string[] = []

const stripNonTranslatable = (text: string) => {
  let rest = text.replace(/\{\w+\}/g, '')
  for (const term of PROTECTED_TERMS) rest = rest.split(term).join('')
  return rest.replace(/[\s\p{P}\p{N}\p{S}]/gu, '')
}

describe('message catalogs', () => {
  for (const namespace of namespaces) {
    describe(namespace, () => {
      const en = leaves(catalogOf('en', namespace))
      const ar = leaves(catalogOf('ar', namespace))
      const arByPath = new Map(ar.map((leaf) => [leaf.path, leaf.value]))

      it('has the same keys in Arabic as in English', () => {
        expect(ar.map((leaf) => leaf.path).sort()).toEqual(en.map((leaf) => leaf.path).sort())
      })

      it('uses a plural object in Arabic exactly where English has one', () => {
        for (const { path, value } of en) {
          expect(typeof arByPath.get(path) === 'string', path).toBe(typeof value === 'string')
        }
      })

      it('gives every Arabic plural object all six forms', () => {
        for (const { path, value } of ar) {
          if (typeof value === 'string') continue
          for (const form of ['zero', 'one', 'two', 'few', 'many', 'other'] as const) {
            expect(typeof value[form], `${namespace}.${path}.${form}`).toBe('string')
            expect(value[form]?.length, `${namespace}.${path}.${form}`).toBeGreaterThan(0)
          }
        }
      })

      it('keeps every placeholder from the English text in the Arabic text', () => {
        const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map((m) => m[1])
        for (const { path, value } of en) {
          const arabic = arByPath.get(path)!
          const required = new Set(
            textsOf(value)
              .flatMap(placeholders)
              .filter((name) => name !== 'count'),
          )
          for (const name of required) {
            for (const text of textsOf(arabic)) {
              expect(text, `${namespace}.${path} must keep {${name}}`).toContain(`{${name}}`)
            }
          }
        }
      })

      it('does not copy English text into Arabic', () => {
        for (const { path, value } of en) {
          if (IDENTICAL_ALLOW_LIST.includes(`${namespace}.${path}`)) continue
          const arabic = arByPath.get(path)!
          const englishTexts = textsOf(value)
          for (const text of textsOf(arabic)) {
            if (stripNonTranslatable(text) === '') continue
            expect(
              englishTexts,
              `${namespace}.${path} is English text in the Arabic catalog`,
            ).not.toContain(text)
          }
        }
      })

      it('has no empty message', () => {
        for (const { path, value } of [...en, ...ar]) {
          for (const text of textsOf(value))
            expect(text.trim().length, `${namespace}.${path}`).toBeGreaterThan(0)
        }
      })
    })
  }
})
