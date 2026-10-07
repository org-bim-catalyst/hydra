// specs/067 Phase 13 (T210) — the admin frame: sidebar, section names, header controls and the Jobs launcher.
export const enAdminShell = {
  sidebar: {
    title: 'Admin',
    navLabel: 'Admin sections',
    expand: 'Expand sidebar',
    collapse: 'Collapse sidebar',
  },
  nav: {
    dashboard: 'Dashboard',
    users: 'Users',
    roles: 'Roles',
    roleAssignments: 'Role assignments',
    systemAgents: 'System agents',
    aiProviders: 'AI providers',
    defaultModels: 'Default models',
    aiCapabilities: 'AI capabilities',
    voice: 'Voice',
    appearance: 'Appearance',
    agentPolicies: 'Agent policies',
    workflowPolicies: 'Workflow policies',
    mcpServers: 'MCP servers',
    operationalFailures: 'Operational failures',
    notifications: 'Notifications',
    deliveries: 'Deliveries',
    announcements: 'Announcements',
    templates: 'Templates',
    localization: 'Localization',
    jobs: 'Jobs',
  },
  badges: {
    criticalCountError: 'Could not load the unacknowledged critical count',
    criticalCountErrorHidden: '(could not load the unacknowledged critical count)',
    criticalCount: '({count} unacknowledged critical)',
    dictationSuspended: '(dictation is suspended)',
  },
  jobs: {
    popupBlocked:
      'Your browser blocked the Jobs dashboard from opening. Allow pop-ups for this site and try again.',
    openFailed: 'Could not open the Jobs dashboard. Please try again.',
  },
} as const
