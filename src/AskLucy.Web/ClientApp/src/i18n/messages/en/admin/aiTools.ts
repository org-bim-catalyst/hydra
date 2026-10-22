// specs/083 — admin 'aiTools' catalog (English): the list of tools the AI can call, and the
// settings each of them can be configured with.
export const enAdminAiTools = {
  title: 'AI tools',
  subtitle:
    'The tools available to the AI — the catalogue is read-only; each tool’s own defaults sit behind its gear',
  errors: { load: 'Could not load AI tools.' },
  table: {
    name: 'Tool',
    origin: 'Origin',
    description: 'Description',
    risk: 'Risk',
    permissions: 'Required permissions',
    empty: 'No tools are currently registered.',
  },
  columns: {
    settings: 'Settings',
  },
  row: {
    configure: 'Configure {tool}',
    nothingToConfigure: 'Nothing to configure',
    settingsFor: 'Settings for {tool}',
  },
  dialog: {
    title: '{tool} settings',
    cancel: 'Cancel',
    save: 'Save',
    saving: 'Saving…',
    failed: 'Something went wrong. Please try again.',
    viewOnly:
      'You can see these settings but not change them. Changing them needs the Manage AI tools permission.',
  },
  feedback: {
    settingsSaved: 'Tool settings saved.',
    failed: 'The settings could not be saved. Please try again.',
  },
  settingsLoadFailed:
    'Could not load the tool settings, so every settings button is disabled.',
  retry: 'Retry',
  risk: {
    Low: 'Low',
    Medium: 'Medium',
    High: 'High',
    Critical: 'Critical',
  },
  origin: {
    native: 'Built-in',
    mcp: 'MCP',
  },
} as const
