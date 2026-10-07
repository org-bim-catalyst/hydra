import type { MessageTree } from '../../../types'

/** Workflow auto-approval policies admin screen (specs/067 Phase 13, T216). */
export const enAdminWorkflowPolicies = {
  page: {
    title: 'Workflow policies',
    subtitle:
      'Pre-approve specific high-risk workflow steps so they run without an interactive approval prompt',
  },
  panel: {
    newPolicy: 'New policy',
    columns: {
      name: 'Name',
      nodeType: 'Node Type',
      underlyingTool: 'Underlying Tool',
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
  nodeTypes: {
    AiPrompt: 'AiPrompt',
    AiAgent: 'AiAgent',
    RagSearch: 'RagSearch',
    MemorySearch: 'MemorySearch',
    DocumentProcessing: 'DocumentProcessing',
    FileOperation: 'FileOperation',
    McpTool: 'McpTool',
    NativeTool: 'NativeTool',
    HumanApproval: 'HumanApproval',
  },
  dialog: {
    title: 'New policy',
    name: 'Name',
    nodeType: 'Node Type (optional)',
    nodeTypeHelp: 'Leave unset to target by underlying tool name alone',
    none: 'None',
    underlyingToolName: 'Underlying Tool Name (optional)',
    underlyingToolNameHelp:
      "Must exactly match the underlying capability's registered tool name, e.g. KnowledgeSearchTool",
    description: 'Description',
    conditions: 'Conditions (JSON, optional)',
    conditionsHelp:
      'A flat JSON object of required parameter values, e.g. {"visibility":"public"}. Leave empty to match every matching node.',
    cancel: 'Cancel',
    create: 'Create Policy',
  },
} satisfies MessageTree
