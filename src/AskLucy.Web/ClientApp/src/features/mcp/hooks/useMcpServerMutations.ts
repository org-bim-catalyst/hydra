import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as mcpServersApi from '../api/mcpServersApi'
import type { ActivateMcpToolInput, RegisterMcpServerInput, UpdateMcpServerInput } from '../api/mcpServersApi'
import { MCP_SERVERS_QUERY_KEY } from './useMcpServers'

export function useRegisterMcpServer() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: RegisterMcpServerInput) => mcpServersApi.registerMcpServer(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}

export function useUpdateMcpServer() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateMcpServerInput }) => mcpServersApi.updateMcpServer(id, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}

export function useDeleteMcpServer() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => mcpServersApi.deleteMcpServer(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}

export function useEnableMcpServer() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => mcpServersApi.enableMcpServer(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}

export function useDisableMcpServer() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => mcpServersApi.disableMcpServer(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}

export function useTestMcpServerConnection() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => mcpServersApi.testMcpServerConnection(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}

export function useRefreshMcpCapabilities() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => mcpServersApi.refreshMcpCapabilities(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}

export function useRotateMcpServerCredential() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, credential }: { id: string; credential: string }) => mcpServersApi.rotateMcpServerCredential(id, credential),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}

export function useActivateMcpTool() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ serverId, toolId, input }: { serverId: string; toolId: string; input: ActivateMcpToolInput }) =>
      mcpServersApi.activateMcpTool(serverId, toolId, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}

export function useDeactivateMcpTool() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ serverId, toolId }: { serverId: string; toolId: string }) => mcpServersApi.deactivateMcpTool(serverId, toolId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: MCP_SERVERS_QUERY_KEY }),
  })
}
