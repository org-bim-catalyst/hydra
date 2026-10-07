// specs/067 Phase 13 (T213) — admin 'roles' catalog (English): the Roles screen, its dialogs and the permission picker.
export const enAdminRoles = {
  title: 'Roles',
  subtitle: { one: '{count} role', other: '{count} roles' },
  bulk: { action: 'Delete', progress: 'Deleting' },
  createRole: 'Create role',
  search: 'Search by name',
  errors: {
    load: 'Could not load roles.',
    loadContentAccess: 'Could not load whether Administrators may view user content.',
    bulkPrepare: 'Could not prepare the bulk delete. Please try again.',
  },
  contentAccess: { label: 'Administrators may view user content' },
  selection: {
    selectedCount: '{count} selected',
    deleteSelected: 'Delete selected',
    selectAll: 'Select all custom roles on this page',
    selectRole: 'Select {name}',
  },
  table: {
    name: 'Name',
    description: 'Description',
    permissions: 'Permissions',
    users: 'Users',
    actions: 'Actions',
    empty: 'No roles found.',
    builtIn: 'Built-in',
    default: 'Default',
    defaultHint: "Every account's starting role, and where users go when their role is deleted",
    permissionCount: { one: '{count} permission', other: '{count} permissions' },
    viewPermissions: 'View permissions',
    viewPermissionsFor: 'View permissions for {name}',
    actionsFor: 'Actions for {name}',
  },
  pagination: {
    rowsPerPage: 'Rows per page:',
    displayedRows: '{from}–{to} of {count}',
    first: 'Go to first page',
    previous: 'Go to previous page',
    next: 'Go to next page',
    last: 'Go to last page',
  },
  menu: {
    edit: 'Edit…',
    duplicate: 'Duplicate…',
    delete: 'Delete…',
    deleteSuperUserOnly: 'Delete (Super User only)',
  },
  editor: {
    createTitle: 'Create role',
    editTitle: 'Edit {name}',
    name: 'Name',
    description: 'Description',
    defaultNameHint: "Every account's starting role — it can't be renamed.",
    nameLength: 'Role name must be between 2 and 50 characters.',
    permissionRequired: 'Select at least one permission.',
    cancel: 'Cancel',
    save: 'Save',
    create: 'Create',
  },
  deleteDialog: {
    title: 'Delete {name}?',
    usersMoved: {
      one: '{count} user currently holds this role and will be moved to the {role} role.',
      other: '{count} users currently hold this role and will be moved to the {role} role.',
    },
    noUsers: 'No users currently hold this role.',
    irreversible: 'This cannot be undone.',
    cancel: 'Cancel',
    confirm: 'Delete',
  },
  duplicateDialog: {
    title: 'Duplicate {name}',
    copyPrefix: 'Copy of',
    noPermissions:
      '{name} has no permissions to copy. Add a permission to it first, or create a new role instead.',
    summary: {
      one: "Saves a new custom role with {name}'s {count} permission. No users are moved to it.",
      other: "Saves a new custom role with {name}'s {count} permissions. No users are moved to it.",
    },
    name: 'Name',
    description: 'Description',
    nameLength: 'Role name must be between 2 and 50 characters.',
    cancel: 'Cancel',
    confirm: 'Duplicate',
  },
  permissionsDialog: {
    title: 'Permissions for {name}',
    none: 'This role has no permissions granted.',
    search: 'Search permissions',
    noMatch: 'No permissions match “{query}”.',
    close: 'Close',
  },
  picker: {
    superUserOnly: 'Only a Super User can grant this',
    basic: "A basic permission — it can't be removed from this role",
    levelView: 'View',
    levelManage: 'Manage',
  },
  areas: {
    Dashboard: 'Dashboard',
    Users: 'Users',
    AiProviders: 'AI providers',
    DefaultModels: 'Default models',
    AiCapabilities: 'AI capabilities',
    AgentPolicies: 'Agent policies',
    SystemAgents: 'System agents',
    WorkflowPolicies: 'Workflow policies',
    McpServers: 'MCP servers',
    CustomModels: 'Custom models',
    OperationalFailures: 'Operational failures',
    Notifications: 'Notifications',
    Appearance: 'Appearance',
  },
  permissions: {
    dashboardView: { name: 'View dashboard', description: 'View the admin dashboard overview.' },
    usersView: { name: 'View users', description: 'View user list and details.' },
    usersManage: { name: 'Manage users', description: 'Create, edit, suspend, or delete users.' },
    aiProvidersView: {
      name: 'View AI providers',
      description: 'View registered AI provider configurations.',
    },
    aiProvidersManage: {
      name: 'Manage AI providers',
      description: 'Add, edit, or remove AI provider credentials.',
    },
    defaultModelsView: {
      name: 'View default models',
      description: "View each provider's default model.",
    },
    defaultModelsManage: {
      name: 'Manage default models',
      description: "Change any provider's default model.",
    },
    aiCapabilitiesView: {
      name: 'View AI capabilities',
      description: 'View which provider serves each AI capability.',
    },
    aiCapabilitiesManage: {
      name: 'Manage AI capabilities',
      description: 'Change which provider serves each AI capability.',
    },
    agentPoliciesView: {
      name: 'View agent policies',
      description: 'View agent execution policy configurations.',
    },
    agentPoliciesManage: {
      name: 'Manage agent policies',
      description: 'Create or edit agent execution policies.',
    },
    systemAgentsView: { name: 'View system agents', description: 'View registered system agents.' },
    workflowPoliciesView: {
      name: 'View workflow policies',
      description: 'View workflow execution policy configurations.',
    },
    workflowPoliciesManage: {
      name: 'Manage workflow policies',
      description: 'Create or edit workflow execution policies.',
    },
    mcpServersView: {
      name: 'View MCP servers',
      description: 'View registered Model Context Protocol servers.',
    },
    mcpServersManage: {
      name: 'Manage MCP servers',
      description: 'Add, edit, or remove MCP server registrations.',
    },
    customModelsView: {
      name: 'View custom models',
      description: 'View custom model deployments and their progress.',
    },
    customModelsManage: {
      name: 'Manage custom models',
      description:
        'Deploy models from Hugging Face to the production server, cancel deployments, and change model availability.',
    },
    operationalFailuresView: {
      name: 'View operational failures',
      description:
        'View the operational failure trail, with metadata-only links to affected chats, workflow runs and documents.',
    },
    operationalFailuresManage: {
      name: 'Manage operational failures',
      description: 'Acknowledge, resolve, and reopen operational failure incidents.',
    },
    operationalFailuresContentView: {
      name: 'View user content in failure investigations',
      description:
        "Read the full content of another user's chat, workflow run or document from a failure incident. Every access is audited. Only a Super User can grant or revoke it; it grants nothing without View operational failures.",
    },
    notificationsView: {
      name: 'View notifications',
      description:
        'View notification templates, delivery history, failed deliveries, channel health and localization settings.',
    },
    notificationsManage: {
      name: 'Manage notifications',
      description:
        'Edit and publish notification templates, retry failed deliveries, publish system announcements, and change localization settings.',
    },
    appearanceView: {
      name: 'View appearance',
      description: 'View the presence sphere settings and their preview.',
    },
    appearanceManage: {
      name: 'Manage appearance',
      description:
        "Change the presence sphere's dot size, size within its card, and whether it can be zoomed.",
    },
  },
} as const
