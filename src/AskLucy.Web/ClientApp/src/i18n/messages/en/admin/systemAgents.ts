// specs/067 Phase 13 (T214) — admin 'systemAgents' catalog (English).
export const enAdminSystemAgents = {
  title: 'System agents',
  subtitle: "The platform's own provisioned agents — read-only, never user-editable",
  errors: { load: 'Could not load system agents.' },
  table: {
    name: 'Name',
    origin: 'Origin',
    systemKey: 'System key',
    status: 'Status',
    version: 'Version',
    lastUpdated: 'Last updated',
    empty: 'No system agents are currently provisioned.',
    provisioned: 'Provisioned by Ask Lucy',
  },
  status: {
    Draft: 'Draft',
    Published: 'Published',
    Archived: 'Archived',
  },
} as const
