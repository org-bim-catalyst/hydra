/**
 * Tab order for `ChatSettingsPage`, kept in one place for the same reason
 * `SETTINGS_TAB_INDEX` is: the page, the account menu and any deep link all read it, so none of
 * them can drift out of sync with the actual tab order.
 */
export const CHAT_SETTINGS_TAB_INDEX = {
  Voice: 0,
  // 1 and 2 were the separate "Chat Configuration" and "Chat History" tabs, merged into the
  // single "Chat" tab below — left unused rather than reused, so no stale deep link at either
  // old index silently lands on the wrong tab.
  /** Moved here from Settings (SETTINGS_TAB_INDEX) — appended, not inserted, so existing tab
   * indices never shift. */
  Viewer: 3,
  /** Merged "Chat Configuration" + "Chat History" — appended for the same reason as Viewer. */
  Chat: 4,
} as const
