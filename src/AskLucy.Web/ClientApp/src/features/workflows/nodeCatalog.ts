import type { WorkflowNodeType } from './api/workflowsApi'

/** spec.md's exact node category list — every `WorkflowNodeType` belongs to exactly one. */
export type WorkflowNodeCategory =
  | 'AI'
  | 'Knowledge'
  | 'Documents'
  | 'Files'
  | 'Tools'
  | 'Control Flow'
  | 'Human Interaction'
  | 'Data Transformation'
  | 'Integration'

export interface WorkflowNodeCatalogEntry {
  nodeType: WorkflowNodeType
  category: WorkflowNodeCategory
  label: string
  description: string
  /** Seeded onto a newly-dropped node — the author edits these in {@link NodeConfigPanel}. */
  defaultConfigurationJson: string
  defaultInputSchemaJson: string
  defaultOutputSchemaJson: string
}

export const WORKFLOW_NODE_CATALOG: readonly WorkflowNodeCatalogEntry[] = [
  {
    nodeType: 'Start',
    category: 'Control Flow',
    label: 'Start',
    description: 'Defines the workflow’s declared inputs. Exactly one per workflow.',
    defaultConfigurationJson: '{}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
  {
    nodeType: 'End',
    category: 'Control Flow',
    label: 'End',
    description: 'Defines the workflow’s declared outputs, mapped from prior steps.',
    defaultConfigurationJson: '{"outputs":{}}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
  {
    nodeType: 'AiPrompt',
    category: 'AI',
    label: 'AI Prompt',
    description: 'Executes an existing saved Prompt through the selected provider/model.',
    defaultConfigurationJson: '{"promptId":"","providerId":"","modelId":"","variableValues":{},"outputField":"text"}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{"type":"object","properties":{"text":{"type":"string"}}}',
  },
  {
    nodeType: 'AiAgent',
    category: 'AI',
    label: 'AI Agent',
    description: 'Invokes an existing published Agent as a single step.',
    defaultConfigurationJson: '{"agentId":"","objective":""}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{"type":"object","properties":{"text":{"type":"string"}}}',
  },
  {
    nodeType: 'RagSearch',
    category: 'Knowledge',
    label: 'RAG Search',
    description: 'Queries the Knowledge Base engine for grounded context and citations.',
    defaultConfigurationJson: '{"query":"","knowledgeBaseIds":[]}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{"type":"object","properties":{"contextText":{"type":"string"},"citations":{"type":"array"}}}',
  },
  {
    nodeType: 'MemorySearch',
    category: 'Knowledge',
    label: 'Memory Search',
    description: 'Retrieves the caller’s relevant long-term memories for a query.',
    defaultConfigurationJson: '{"query":""}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{"type":"object","properties":{"contextText":{"type":"string"}}}',
  },
  {
    nodeType: 'DocumentProcessing',
    category: 'Documents',
    label: 'Document Processing',
    description: 'Searches the caller’s own document library for matching documents.',
    defaultConfigurationJson: '{"query":""}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{"type":"object","properties":{"documents":{"type":"array"}}}',
  },
  {
    nodeType: 'FileOperation',
    category: 'Files',
    label: 'File Operation',
    description: 'Reads a document’s content or metadata (read-only).',
    defaultConfigurationJson: '{"operation":"Read","documentId":""}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
  {
    nodeType: 'McpTool',
    category: 'Integration',
    label: 'MCP Tool',
    description: 'Executes a tool exposed by a connected MCP server.',
    defaultConfigurationJson: '{"toolName":"mcp:","input":{}}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
  {
    nodeType: 'NativeTool',
    category: 'Tools',
    label: 'Native Tool',
    description: 'Executes a built-in platform tool (e.g. Conversation lookup).',
    defaultConfigurationJson: '{"toolName":"","input":{}}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
  {
    nodeType: 'Transform',
    category: 'Data Transformation',
    label: 'Transform',
    description: 'Maps a single field via a sandboxed workflow expression.',
    defaultConfigurationJson: '{"expression":"","outputField":"result"}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
  {
    nodeType: 'Condition',
    category: 'Control Flow',
    label: 'Condition',
    description: 'Branches execution based on a boolean workflow expression.',
    defaultConfigurationJson: '{"expression":""}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
  {
    nodeType: 'Parallel',
    category: 'Control Flow',
    label: 'Parallel',
    description: 'Runs independent branches concurrently.',
    defaultConfigurationJson: '{"maxConcurrency":4}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
  {
    nodeType: 'Merge',
    category: 'Control Flow',
    label: 'Merge',
    description: 'Combines parallel branch outputs (All Completed / First Completed / Any Completed / Collect All).',
    defaultConfigurationJson: '{"strategy":"AllCompleted","branchNodeKeys":[]}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{"type":"object"}',
  },
  {
    nodeType: 'HumanApproval',
    category: 'Human Interaction',
    label: 'Human Approval',
    description: 'Pauses execution until an authorized user approves, rejects, or requests changes.',
    defaultConfigurationJson: '{"timeoutSeconds":null}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
  {
    nodeType: 'Validation',
    category: 'Data Transformation',
    label: 'Validation',
    description: 'Validates data against a boolean expression or a JSON Schema.',
    defaultConfigurationJson: '{"expression":""}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{"type":"object","properties":{"valid":{"type":"boolean"}}}',
  },
  {
    nodeType: 'Delay',
    category: 'Control Flow',
    label: 'Delay',
    description: 'Architecture placeholder for future scheduling/delay capabilities.',
    defaultConfigurationJson: '{}',
    defaultInputSchemaJson: '{}',
    defaultOutputSchemaJson: '{}',
  },
] as const

export const WORKFLOW_NODE_CATEGORIES: readonly WorkflowNodeCategory[] = [
  'AI',
  'Knowledge',
  'Documents',
  'Files',
  'Tools',
  'Control Flow',
  'Human Interaction',
  'Data Transformation',
  'Integration',
]

export function getNodeCatalogEntry(nodeType: WorkflowNodeType): WorkflowNodeCatalogEntry {
  const entry = WORKFLOW_NODE_CATALOG.find((e) => e.nodeType === nodeType)
  if (!entry) throw new Error(`Unknown workflow node type '${nodeType}'.`)
  return entry
}

/** The JSON Schema `type` keyword's numeric variants are treated as mutually compatible; every other pairing must match exactly. */
const NUMERIC_SCHEMA_TYPES = new Set(['number', 'integer'])

/** Reads a schema document's own top-level `"type"` keyword, if it declares a scalar one — `null` for missing/malformed/`object`/`array` schemas, which carry no single connectable type to compare (FR-008; mirrors `WorkflowGraphValidator`'s server-side rule exactly). */
export function scalarSchemaType(schemaJson: string): string | null {
  try {
    const parsed = JSON.parse(schemaJson) as { type?: unknown }
    const type = typeof parsed?.type === 'string' ? parsed.type : null
    return type && type !== 'object' && type !== 'array' ? type : null
  } catch {
    return null
  }
}

/** FR-008 — a connection is rejected only when BOTH endpoints declare an explicit, differing scalar type; an undeclared ("Any") side never blocks a connection. */
export function areSchemaTypesCompatible(sourceOutputSchemaJson: string, targetInputSchemaJson: string): boolean {
  const sourceType = scalarSchemaType(sourceOutputSchemaJson)
  const targetType = scalarSchemaType(targetInputSchemaJson)
  if (sourceType === null || targetType === null) return true
  if (sourceType === targetType) return true
  return NUMERIC_SCHEMA_TYPES.has(sourceType) && NUMERIC_SCHEMA_TYPES.has(targetType)
}
