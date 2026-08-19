import type { WorkflowNodeApprovalPolicy, WorkflowNodeType, WorkflowVariableKind, WorkflowVariableType } from './api/workflowsApi'

/**
 * The exact JSON shape `Workflow.DraftDefinitionJson` round-trips through
 * (`WorkflowDraftDefinition`/`WorkflowDraftNode`/`WorkflowDraftConnection`/`WorkflowDraftVariable`
 * in `src/AskLucy.Application/Workflows/Validation/WorkflowDraftDefinition.cs`) — field names and
 * casing must match verbatim (`System.Text.Json`'s Web defaults camelCase the C# record's
 * PascalCase property names).
 */
export interface WorkflowDraftNodeDefinition {
  nodeKey: string
  nodeType: WorkflowNodeType
  name: string
  description: string | null
  inputSchemaJson: string
  outputSchemaJson: string
  configurationJson: string
  requiredPermissionsJson: string
  timeoutSeconds: number | null
  retryPolicyJson: string | null
  approvalPolicy: WorkflowNodeApprovalPolicy
  idempotencyKeyExpression: string | null
  compensatingNodeKey: string | null
  canvasX: number
  canvasY: number
}

export interface WorkflowDraftConnectionDefinition {
  sourceNodeKey: string
  targetNodeKey: string
  branchLabel: string | null
  typeContract: string | null
}

export interface WorkflowDraftVariableDefinition {
  name: string
  kind: WorkflowVariableKind
  valueType: WorkflowVariableType
  defaultValueJson: string | null
  isRequired: boolean
}

export interface WorkflowDraftDefinition {
  inputsSchemaJson: string
  outputsSchemaJson: string
  errorPolicyJson: string
  executionPolicyJson: string
  securityPolicyJson: string
  nodes: WorkflowDraftNodeDefinition[]
  connections: WorkflowDraftConnectionDefinition[]
  variables: WorkflowDraftVariableDefinition[]
}

export const EMPTY_DRAFT_DEFINITION: WorkflowDraftDefinition = {
  inputsSchemaJson: '{}',
  outputsSchemaJson: '{}',
  errorPolicyJson: '{"strategy":"Stop"}',
  executionPolicyJson: '{}',
  securityPolicyJson: '{}',
  nodes: [],
  connections: [],
  variables: [],
}

/** Tolerant of an empty/unset draft (a brand-new workflow) and of malformed JSON left over from a failed prior save — both fall back to {@link EMPTY_DRAFT_DEFINITION} rather than crashing the Designer. */
export function parseDraftDefinition(draftDefinitionJson: string): WorkflowDraftDefinition {
  if (!draftDefinitionJson || draftDefinitionJson.trim().length === 0) {
    return EMPTY_DRAFT_DEFINITION
  }

  try {
    const parsed = JSON.parse(draftDefinitionJson) as Partial<WorkflowDraftDefinition>
    return {
      inputsSchemaJson: parsed.inputsSchemaJson ?? '{}',
      outputsSchemaJson: parsed.outputsSchemaJson ?? '{}',
      errorPolicyJson: parsed.errorPolicyJson ?? '{"strategy":"Stop"}',
      executionPolicyJson: parsed.executionPolicyJson ?? '{}',
      securityPolicyJson: parsed.securityPolicyJson ?? '{}',
      nodes: parsed.nodes ?? [],
      connections: parsed.connections ?? [],
      variables: parsed.variables ?? [],
    }
  } catch {
    return EMPTY_DRAFT_DEFINITION
  }
}
