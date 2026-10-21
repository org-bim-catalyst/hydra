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
