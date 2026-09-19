/**
 * The client-side mirror of the ASP.NET Identity password policy configured in
 * `AskLucy.Persistence/DependencyInjection.cs`. The server stays authoritative — a rejection
 * still comes back per rule in the Problem Details `errors` bag — but the rules have to be
 * stated here too, because a checklist the user can watch tick off as they type is the whole
 * point: a single sentence listing four rules reads as an accusation once a submit has failed.
 *
 * If the Identity options change, change this list in the same commit.
 */
export interface PasswordRule {
  id: string
  label: string
  isMet: (password: string) => boolean
}

export const passwordRules: readonly PasswordRule[] = [
  { id: 'length', label: 'At least 8 characters', isMet: (p) => p.length >= 8 },
  { id: 'uppercase', label: 'An uppercase letter', isMet: (p) => /[A-Z]/.test(p) },
  { id: 'lowercase', label: 'A lowercase letter', isMet: (p) => /[a-z]/.test(p) },
  { id: 'digit', label: 'A number', isMet: (p) => /[0-9]/.test(p) },
  // Identity's own definition of "non-alphanumeric": anything that is not a letter or a digit.
  { id: 'symbol', label: 'A symbol (for example ! ? # $)', isMet: (p) => /[^a-zA-Z0-9]/.test(p) },
]

export type PasswordStrength = 'weak' | 'fair' | 'good' | 'strong'

/** How many of the four strength segments are lit, 0-4. */
export interface PasswordStrengthResult {
  score: 0 | 1 | 2 | 3 | 4
  label: PasswordStrength
  /** Every rule, with whether this password satisfies it — drives the checklist. */
  rules: { id: string; label: string; met: boolean }[]
  allRulesMet: boolean
}

function scoreFor(length: number, allRulesMet: boolean, metCount: number): 0 | 1 | 2 | 3 | 4 {
  if (length === 0) return 0
  // Below the policy the bar can never read better than "fair", however long the password is.
  if (!allRulesMet) return metCount >= 4 ? 2 : 1
  return length >= 16 ? 4 : 3
}

/**
 * Deliberately not a real entropy estimate. Satisfying the policy is the pass/fail signal and the
 * checklist already carries it; the bar's job is only to nudge past the minimum, so it rewards
 * the policy rules plus length and nothing subtler. Anything cleverer (dictionary lookups,
 * zxcvbn) would mean shipping a word list to say something the checklist already says.
 */
export function evaluatePassword(password: string): PasswordStrengthResult {
  const rules = passwordRules.map((rule) => ({ id: rule.id, label: rule.label, met: rule.isMet(password) }))
  const allRulesMet = rules.every((rule) => rule.met)
  const metCount = rules.filter((rule) => rule.met).length

  const score = scoreFor(password.length, allRulesMet, metCount)

  const label: PasswordStrength = score >= 4 ? 'strong' : score === 3 ? 'good' : score === 2 ? 'fair' : 'weak'
  return { score, label, rules, allRulesMet }
}

/**
 * Client-side gate for the submit button. Not a substitute for the server's check — it only spares
 * the user a round trip that was always going to come back with the same four rules.
 */
export function isPasswordPolicyMet(password: string): boolean {
  return passwordRules.every((rule) => rule.isMet(password))
}
