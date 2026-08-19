/**
 * specs/025-chat-configuration-settings research.md Decision 4 — a single source of truth
 * for `SettingsPage`'s tab order, referenced by `SettingsPage` itself, Chat Configuration's
 * entry-point links, and both account menus, so none of them can drift out of sync with the
 * actual tab order.
 */
export const SETTINGS_TAB_INDEX = {
  Security: 0,
  Account: 1,
  AiProviders: 2,
  Voice: 3,
  ChatConfiguration: 4,
  ChatHistory: 5,
  Data: 6,
  Cookies: 7,
  /** specs/028-ai-floating-panels — appended, not inserted, so existing tab indices never
   * shift (research.md Decision 6). */
  Viewer: 8,
} as const
