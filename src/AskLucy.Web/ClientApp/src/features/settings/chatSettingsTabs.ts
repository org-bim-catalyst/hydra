/**
 * Tab order for `ChatSettingsPage`, kept in one place for the same reason
 * `SETTINGS_TAB_INDEX` is: the page, the account menu and any deep link all read it, so none of
 * them can drift out of sync with the actual tab order.
 */
export const CHAT_SETTINGS_TAB_INDEX = {
  Voice: 0,
  ChatConfiguration: 1,
  ChatHistory: 2,
} as const
