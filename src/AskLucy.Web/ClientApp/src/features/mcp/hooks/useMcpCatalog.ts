import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as mcpCatalogApi from '../api/mcpCatalogApi'
import { PROMPTS_QUERY_KEY } from '../../prompts/hooks/usePrompts'

const MCP_CATALOG_QUERY_KEY = ['mcp-catalog']

/** spec.md FR-062 — the tools any authenticated user could enable for their own agent. */
export function useMcpCatalogTools() {
  return useQuery({
    queryKey: [...MCP_CATALOG_QUERY_KEY, 'tools'],
    queryFn: () => mcpCatalogApi.listAvailableMcpTools(),
  })
}

export function useMcpCatalogTool(namespacedName: string | null) {
  return useQuery({
    queryKey: [...MCP_CATALOG_QUERY_KEY, 'tools', namespacedName],
    queryFn: () => mcpCatalogApi.getMcpTool(namespacedName!),
    enabled: namespacedName !== null,
  })
}

/** spec.md FR-036 — resources an agent could fetch on the user's behalf. */
export function useMcpCatalogResources() {
  return useQuery({
    queryKey: [...MCP_CATALOG_QUERY_KEY, 'resources'],
    queryFn: () => mcpCatalogApi.listAvailableMcpResources(),
  })
}

/** spec.md FR-042 — MCP-sourced prompts only (research.md Decision 16); merge with native prompts wherever a unified picker exists. */
export function useMcpCatalogPrompts() {
  return useQuery({
    queryKey: [...MCP_CATALOG_QUERY_KEY, 'prompts'],
    queryFn: () => mcpCatalogApi.listAvailableMcpPrompts(),
  })
}

export function useDuplicateMcpPrompt() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (namespacedName: string) => mcpCatalogApi.duplicateMcpPrompt(namespacedName),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY }),
  })
}
