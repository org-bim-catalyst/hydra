/** The languages the platform ships catalogs for (FR-045). A new language is added here and under `messages/`. */
export const LANGUAGES = ['en', 'ar'] as const
export type Language = (typeof LANGUAGES)[number]
export type Direction = 'ltr' | 'rtl'

export const DEFAULT_LANGUAGE: Language = 'en'

export const isLanguage = (code: string): code is Language =>
  (LANGUAGES as readonly string[]).includes(code)

/** Right-to-left scripts. The server also returns `direction`; this is the fallback when it is absent. */
export const DIRECTION_OF: Record<Language, Direction> = { en: 'ltr', ar: 'rtl' }

/**
 * A message that varies with a `count` parameter, resolved with `Intl.PluralRules`. English needs `one` and
 * `other`; Arabic needs all six forms (asserted by `catalogCompleteness.test.ts`).
 */
export interface PluralMessage {
  zero?: string
  one: string
  two?: string
  few?: string
  many?: string
  other: string
}

export type Message = string | PluralMessage

export interface MessageTree {
  [key: string]: Message | MessageTree
}

/**
 * The shape another language's catalog must have to match `T` (the English catalog): same keys at every level,
 * a string where English has a string, a plural object where English has one. Use as
 * `export const ar = { … } satisfies MessagesOf<typeof en>`; a missing or extra key then fails `tsc` (SC-015).
 */
export type MessagesOf<T> = {
  [K in keyof T]: T[K] extends string
    ? string
    : T[K] extends { one: string; other: string }
      ? PluralMessage
      : MessagesOf<T[K]>
}

/** Every dotted path to a message in a catalog, e.g. `'deliveries.retrySucceeded'`. */
export type MessageKey<T> = {
  [K in keyof T & string]: T[K] extends string
    ? K
    : T[K] extends { one: string; other: string }
      ? K
      : `${K}.${MessageKey<T[K]>}`
}[keyof T & string]

export type MessageParams = Record<string, string | number>
