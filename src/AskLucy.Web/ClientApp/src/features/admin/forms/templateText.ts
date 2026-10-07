// specs/067 US7 — the same rules the server applies to template text (FR-041, FR-047, FR-050). The server stays the authority; this saves a round trip.

const TOKEN = /\{\{([^{}]*)\}\}/g
const VARIABLE_NAME = /^[a-zA-Z][a-zA-Z0-9_]{0,49}$/
const RAW_URL_OR_HTML = /https?:\/\/|www\.|<[a-zA-Z/]/i

/** The first problem with `value`, or null: too long, a raw link or markup, a malformed or undeclared `{{ token }}`. */
export function templateTextProblem(
  value: string,
  maxLength: number,
  allowed: ReadonlySet<string>,
): string | null {
  if (value.length > maxLength) return `Can be at most ${maxLength} characters.`
  if (RAW_URL_OR_HTML.test(value))
    return 'Links and HTML are not allowed. Links come only from the action button.'
  for (const match of value.matchAll(TOKEN)) {
    const name = (match[1] ?? '').trim()
    if (!VARIABLE_NAME.test(name)) return `'${match[0]}' is not a valid variable. Use {{ name }}.`
    if (name === 'actionUrl')
      return 'The action link is added by the platform and cannot be placed in text.'
    if (!allowed.has(name)) return `'{{ ${name} }}' is not a variable this notification provides.`
  }
  const remainder = value.replace(TOKEN, '')
  if (remainder.includes('{{') || remainder.includes('}}'))
    return "Unmatched '{{' or '}}'. Write each variable as {{ name }}."
  return null
}
