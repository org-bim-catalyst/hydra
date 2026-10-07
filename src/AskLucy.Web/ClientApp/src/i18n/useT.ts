import { useContext, useMemo } from 'react'
import { formatDate, formatNumber, formatRelative, type RelativeOptions } from './format'
import { LanguageContext } from './languageContext'
import { catalogs, type EnglishCatalogs, type Namespace } from './messages'
import {
  DEFAULT_LANGUAGE,
  DIRECTION_OF,
  type Language,
  type Message,
  type MessageKey,
  type MessageParams,
  type MessageTree,
  type PluralMessage,
} from './types'

function isPlural(message: Message | MessageTree | undefined): message is PluralMessage {
  return (
    typeof message === 'object' &&
    message !== null &&
    typeof (message as PluralMessage).other === 'string'
  )
}

function lookup(tree: MessageTree, key: string): Message | undefined {
  let node: Message | MessageTree | undefined = tree
  for (const part of key.split('.')) {
    if (node === undefined || typeof node === 'string' || isPlural(node)) return undefined
    node = node[part]
  }
  return typeof node === 'string' || isPlural(node) ? node : undefined
}

function reportMissing(language: Language, namespace: string, key: string) {
  // The build already guarantees completeness (`satisfies MessagesOf<…>`), so this only fires for a key that
  // was typed past the compiler. It is loud in development and quiet in production, where English is shown.
  if (import.meta.env.DEV) {
    console.error(`[i18n] No "${language}" message for ${namespace}.${key}; showing English.`)
  }
}

/**
 * Params are substituted as plain text. The result is rendered by React as a text node, never as HTML, so user
 * content (names, template variables, announcement text) can only ever be data. In a right-to-left language each
 * string param is wrapped in Unicode isolates (the effect of `<bdi>`), so a Latin-script name or term does not
 * reorder the Arabic punctuation around it.
 */
function interpolate(
  template: string,
  params: MessageParams | undefined,
  isolate: boolean,
): string {
  if (!params) return template
  return template.replace(/\{(\w+)\}/g, (placeholder, name: string) => {
    const value = params[name]
    if (value === undefined) return placeholder
    return isolate && typeof value === 'string' ? `⁨${value}⁩` : String(value)
  })
}

function pick(message: Message, language: Language, params: MessageParams | undefined): string {
  if (typeof message === 'string') return message
  const count = Number(params?.count)
  const category = Number.isFinite(count) ? new Intl.PluralRules(language).select(count) : 'other'
  return message[category] ?? message.other
}

export type Translate<N extends Namespace> = (
  key: MessageKey<EnglishCatalogs[N]>,
  params?: MessageParams,
) => string

/** The translate function for a namespace and language, without React (tests, and code outside components). */
export function createT<N extends Namespace>(namespace: N, language: Language): Translate<N> {
  return (key, params) => {
    const isolate = DIRECTION_OF[language] === 'rtl'
    const own = lookup(catalogs[language][namespace] as MessageTree, key)
    if (own !== undefined) return interpolate(pick(own, language, params), params, isolate)

    reportMissing(language, namespace, key)
    const fallback = lookup(catalogs[DEFAULT_LANGUAGE][namespace] as MessageTree, key)
    return fallback === undefined
      ? key
      : interpolate(pick(fallback, DEFAULT_LANGUAGE, params), params, false)
  }
}

/** `const t = useT('notifications'); t('center.title'); t('bell.labelUnread', { count: 3 })` */
export function useT<N extends Namespace>(namespace: N): Translate<N> {
  const { language } = useContext(LanguageContext)
  return useMemo(() => createT(namespace, language), [namespace, language])
}

/** Number, date and relative-time formatters bound to the surrounding language (Western digits, Gregorian dates). */
export function useFormat() {
  const { language } = useContext(LanguageContext)
  return useMemo(
    () => ({
      language,
      number: (value: number, options?: Intl.NumberFormatOptions) =>
        formatNumber(value, language, options),
      date: (value: Date | string | number, options?: Intl.DateTimeFormatOptions) =>
        formatDate(value, language, options),
      relative: (value: Date | string | number, options?: RelativeOptions) =>
        formatRelative(value, language, options),
    }),
    [language],
  )
}
