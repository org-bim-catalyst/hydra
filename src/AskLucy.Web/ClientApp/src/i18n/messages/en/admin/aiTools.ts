// specs/083 — admin 'aiTools' catalog (English): the read-only list of tools the AI can call.
export const enAdminAiTools = {
  title: 'AI tools',
  subtitle: 'The tools available to the AI — read-only, never editable here',
  errors: { load: 'Could not load AI tools.' },
  table: {
    name: 'Tool',
    origin: 'Origin',
    description: 'Description',
    risk: 'Risk',
    permissions: 'Required permissions',
    empty: 'No tools are currently registered.',
  },
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
