import { apiFetch } from '../../../api/httpClient'

/** specs/083 — mirrors `AgentToolDto`. Read-only. */
export interface AdminAiTool {
  name: string
  description: string
  /** `AgentToolRiskLevel`, serialized as a string API-wide. */
  riskLevel: 'Low' | 'Medium' | 'High' | 'Critical'
  requiredPermissions: string[]
  inputSchemaJson: string
  outputSchemaJson: string
}

/** An MCP tool's name is always namespaced `mcp:{serverId}:{toolName}`; a native tool's is plain. */
export const isMcpTool = (name: string) => name.startsWith('mcp:')

export const getAiTools = () => apiFetch<AdminAiTool[]>('/admin/ai-tools')

/** specs/083 — mirrors `ToolSettingValueType`. */
export type ToolSettingValueType = 'Boolean' | 'Choice' | 'Integer'

/** One permitted value of a `Choice` setting. */
export interface ToolSettingOption {
  value: string
  label: string
}

/**
 * One setting a tool declares. The label and description are server-supplied English — the
 * server owns the wording, so it is rendered verbatim rather than translated here.
 */
export interface ToolSetting {
  key: string
  valueType: ToolSettingValueType
  label: string
  description: string
  /** The value that applies now: the saved one, or the default. */
  value: string
  defaultValue: string
  /** For `Choice`. */
  options: ToolSettingOption[]
  /** For `Integer`. */
  minimum: number | null
  /** For `Integer`. */
  maximum: number | null
}

/** The settings one tool declares. A tool with none is absent from the list. */
export interface ToolSettings {
  tool: string
  settings: ToolSetting[]
}

export const getToolSettings = () => apiFetch<ToolSettings[]>('/admin/ai-tools/settings')

/** Keys left out keep their current value. */
export const updateToolSettings = (tool: string, values: Record<string, string>) =>
  apiFetch<void>(`/admin/ai-tools/${tool}/settings`, {
    method: 'PUT',
    body: JSON.stringify({ values }),
  })
