import { apiFetch } from '../../../api/httpClient'
import type { AgentToolRiskLevel } from './mcpServersApi'
import type { PromptDetail } from '../../prompts/api/promptsApi'

/** contracts/mcp-api.md's `GET /mcp/catalog/tools` shape — mirrors `McpToolCatalogSummaryDto`. */
export interface McpToolCatalogSummary {
  namespacedName: string
  displayName: string
  description: string
  sourceServerName: string
  effectiveRiskLevel: AgentToolRiskLevel
  requiredPermissions: string[]
}

/** mirrors `McpToolDetailDto` — full detail for one catalog tool (FR-020). */
export interface McpToolDetail {
  namespacedName: string
  displayName: string
  description: string
  sourceServerName: string
  inputSchemaJson: string
  outputSchemaJson: string
  declaredCapabilitiesJson: string | null
  effectiveRiskLevel: AgentToolRiskLevel
  requiredPermissions: string[]
  version: string | null
  lastUpdatedAtUtc: string | null
}

/** mirrors `McpResourceCatalogSummaryDto` (FR-036). */
export interface McpResourceCatalogSummary {
  namespacedName: string
  uri: string
  name: string
  description: string | null
  contentType: string | null
  sourceServerName: string
}

/** mirrors `McpPromptCatalogSummaryDto` (FR-042) — MCP-sourced prompts only. */
export interface McpPromptCatalogSummary {
  namespacedName: string
  name: string
  description: string | null
  sourceServerName: string
}

const BASE = '/mcp/catalog'

export const listAvailableMcpTools = () => apiFetch<McpToolCatalogSummary[]>(`${BASE}/tools`)

export const getMcpTool = (namespacedName: string) => apiFetch<McpToolDetail>(`${BASE}/tools/${encodeURIComponent(namespacedName)}`)

export const listAvailableMcpResources = () => apiFetch<McpResourceCatalogSummary[]>(`${BASE}/resources`)

export const listAvailableMcpPrompts = () => apiFetch<McpPromptCatalogSummary[]>(`${BASE}/prompts`)

export const duplicateMcpPrompt = (namespacedName: string) =>
  apiFetch<PromptDetail>(`${BASE}/prompts/${encodeURIComponent(namespacedName)}/actions/duplicate`, { method: 'POST' })
