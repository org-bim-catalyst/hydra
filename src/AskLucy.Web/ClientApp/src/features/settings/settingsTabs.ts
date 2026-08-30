/**
 * specs/025-chat-configuration-settings research.md Decision 4 — a single source of truth
 * for `SettingsPage`'s tab order, referenced by `SettingsPage` itself, Chat Configuration's
 * entry-point links, and both account menus, so none of them can drift out of sync with the
 * actual tab order.
 */
export const SETTINGS_TAB_INDEX = {
  Security: 0,
  Account: 1,
  /**
   * Indices 2 through 5 are deliberately left unused, and the tabs carry explicit values so the
   * gaps hold:
   *   2 — "AI Providers", the per-user default provider/model, moved to the admin panel. Which
   *       model answers a user is a platform decision, configured there as the Chat capability.
   *   3, 4, 5 — Voice, Chat Configuration and Chat History, moved to Chat settings
   *       (see CHAT_SETTINGS_TAB_INDEX). They describe how a conversation behaves and belong
   *       together, not beside password changes and cookie preferences.
   * Renumbering after a removal would silently repoint every saved deep link and both account
   * menus one tab to the left — the class of invisible mis-routing this change set exists to
   * remove.
   */
  Data: 6,
  Cookies: 7,
  /** specs/028-ai-floating-panels — appended, not inserted, so existing tab indices never
   * shift (research.md Decision 6). */
  Viewer: 8,
} as const
