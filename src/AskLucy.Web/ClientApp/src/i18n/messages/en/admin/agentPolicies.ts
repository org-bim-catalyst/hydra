import type { MessageTree } from '../../../types'

/** Agent auto-approval policies admin screen (specs/067 Phase 13, T216). */
export const enAdminAgentPolicies = {
  page: {
    title: 'Agent policies',
    subtitle:
      'Pre-approve specific high-risk agent actions so they run without an interactive approval prompt',
  },
  panel: {
    newPolicy: 'New policy',
    columns: {
      name: 'Name',
      tool: 'Tool',
      conditions: 'Conditions',
      enabled: 'Enabled',
      actions: 'Actions',
    },
    empty: 'No policies configured yet.',
    always: 'Always',
    enableAria: 'Enable {name}',
    deleteAria: 'Delete {name}',
    errors: {
      create: 'Could not create the policy. Please try again.',
      update: 'Could not update the policy. Please try again.',
      delete: 'Could not delete the policy. Please try again.',
    },
  },
  dialog: {
    title: 'New policy',
    name: 'Name',
    toolName: 'Tool Name',
    toolNameHelp: "Must exactly match the tool's registered name, e.g. FakeHighRiskTool",
    description: 'Description',
    conditions: 'Conditions (JSON, optional)',
    conditionsHelp:
      'A flat JSON object of required parameter values, e.g. {"action":"read-only"}. Leave empty to match every call to this tool.',
    cancel: 'Cancel',
    create: 'Create Policy',
  },
} satisfies MessageTree
