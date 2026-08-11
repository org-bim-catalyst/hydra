import { useQuery } from '@tanstack/react-query'
import * as mcpServersApi from '../api/mcpServersApi'
import type { ListMcpServersParams } from '../api/mcpServersApi'

export const MCP_SERVERS_QUERY_KEY = ['admin', 'mcp-servers']

export function useMcpServers(params: ListMcpServersParams = {}) {
  return useQuery({
    queryKey: [...MCP_SERVERS_QUERY_KEY, 'list', params],
    queryFn: () => mcpServersApi.listMcpServers(params),
  })
}

export function useMcpServer(id: string | null) {
  return useQuery({
    queryKey: [...MCP_SERVERS_QUERY_KEY, id],
    queryFn: () => mcpServersApi.getMcpServer(id!),
    enabled: id !== null,
  })
}

/** research.md Decision 13 — polled, not pushed; no SignalR hub for MCP health. */
export function useMcpServerHealth(id: string | null) {
  return useQuery({
    queryKey: [...MCP_SERVERS_QUERY_KEY, id, 'health'],
    queryFn: () => mcpServersApi.getMcpServerHealth(id!),
    enabled: id !== null,
    refetchInterval: 30_000,
  })
}

export function useMcpServerTools(id: string | null) {
  return useQuery({
    queryKey: [...MCP_SERVERS_QUERY_KEY, id, 'tools'],
    queryFn: () => mcpServersApi.listMcpServerTools(id!),
    enabled: id !== null,
  })
}

export function useMcpServerReferences(id: string | null) {
  return useQuery({
    queryKey: [...MCP_SERVERS_QUERY_KEY, id, 'references'],
    queryFn: () => mcpServersApi.listMcpServerReferences(id!),
    enabled: id !== null,
  })
}

export function useMcpAuditLog(id: string | null, cursor: string | null = null) {
  return useQuery({
    queryKey: [...MCP_SERVERS_QUERY_KEY, id, 'audit-log', cursor],
    queryFn: () => mcpServersApi.listMcpAuditLog(id!, cursor),
    enabled: id !== null,
  })
}
