// specs/067 US7 — the same rules the server applies to template text (FR-041, FR-047, FR-050). The server stays the authority; this saves a round trip.
import { createT, type Translate } from '../../../i18n/useT'

const TOKEN = /\{\{([^{}]*)\}\}/g
const VARIABLE_NAME = /^[a-zA-Z][a-zA-Z0-9_]{0,49}$/
const RAW_URL_OR_HTML = /https?:\/\/|www\.|<[a-zA-Z/]/i

const english = createT('admin.notifications', 'en')

/**
 * The first problem with `value`, or null: too long, a raw link or markup, a malformed or undeclared `{{ token }}`.
 * The message comes back in the language of `t` (English when none is given); the variable names in it are data.
 */
export function templateTextProblem(
  value: string,
  maxLength: number,
  allowed: ReadonlySet<string>,
  t: Translate<'admin.notifications'> = english,
): string | null {
  if (value.length > maxLength) return t('templateText.tooLong', { max: maxLength })
  if (RAW_URL_OR_HTML.test(value))
    return t('templateText.linksOrHtml')
  for (const match of value.matchAll(TOKEN)) {
    const name = (match[1] ?? '').trim()
    if (!VARIABLE_NAME.test(name)) return t('templateText.invalidVariable', { token: match[0] })
    if (name === 'actionUrl')
      return t('templateText.actionUrl')
    if (!allowed.has(name)) return t('templateText.undeclaredVariable', { name })
  }
  const remainder = value.replace(TOKEN, '')
  if (remainder.includes('{{') || remainder.includes('}}'))
    return t('templateText.unmatched')
  return null
}
