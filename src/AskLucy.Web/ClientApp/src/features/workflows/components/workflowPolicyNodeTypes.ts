import type { WorkflowNodeType } from '../api/workflowsApi'

/** The node types a policy can target; each has a label in the `admin.workflowPolicies` catalog. */
export const NODE_TYPES = [
  'AiPrompt',
  'AiAgent',
  'RagSearch',
  'MemorySearch',
  'DocumentProcessing',
  'FileOperation',
  'McpTool',
  'NativeTool',
  'HumanApproval',
] as const satisfies readonly WorkflowNodeType[]
