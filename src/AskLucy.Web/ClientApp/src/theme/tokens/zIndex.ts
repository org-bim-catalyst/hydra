/** Named layering hierarchy (FR-006). Values match MUI's own zIndex scale so existing
 * MUI components (Modal, Snackbar, Tooltip, AppBar) keep their default stacking, while
 * giving custom surfaces (the persistent AppShell) an explicit, documented layer to sit
 * at rather than an ad hoc magic number. */
export const zIndex = {
  appShell: 1100,
  dropdown: 1300,
  dialog: 1300,
  snackbar: 1400,
  tooltip: 1500,
} as const
