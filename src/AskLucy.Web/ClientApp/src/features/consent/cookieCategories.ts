/**
 * Single shared source of cookie-category text (specs/004-cookie-consent-privacy, FR-021):
 * the banner, the Settings "Cookies" panel, and the Privacy Page all import from here so
 * category names/descriptions are never hardcoded independently in more than one place.
 */
export interface CookieCategoryInfo {
  key: 'essential' | 'functional' | 'analytics' | 'marketing'
  label: string
  description: string
  /** Essential is always granted and can never be toggled off, in the banner or in Settings. */
  locked: boolean
}

export const COOKIE_CATEGORIES: readonly CookieCategoryInfo[] = [
  {
    key: 'essential',
    label: 'Essential',
    description: 'Required for sign-in, security, and core functionality. Always on.',
    locked: true,
  },
  {
    key: 'functional',
    label: 'Functional',
    description: 'Remembers your preferences, such as theme and language, across visits.',
    locked: false,
  },
  {
    key: 'analytics',
    label: 'Analytics',
    description: 'Helps us understand how Ask Lucy is used so we can improve it.',
    locked: false,
  },
  {
    key: 'marketing',
    label: 'Marketing',
    description: 'Used to personalize communications and measure their effectiveness.',
    locked: false,
  },
] as const
