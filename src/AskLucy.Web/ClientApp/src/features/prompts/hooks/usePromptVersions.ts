import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import * as versionsApi from '../api/promptVersionsApi'
import { PROMPTS_QUERY_KEY } from './usePrompts'

export function useVersions(promptId: string) {
  return useQuery({ queryKey: ['prompts', promptId, 'versions'], queryFn: () => versionsApi.listVersions(promptId) })
}

export function useVersion(promptId: string, versionNumber: number | null) {
  return useQuery({
    queryKey: ['prompts', promptId, 'versions', versionNumber],
    queryFn: () => versionsApi.getVersion(promptId, versionNumber!),
    enabled: versionNumber !== null,
  })
}

export function useCompareVersions(promptId: string, from: number | null, to: number | null) {
  return useQuery({
    queryKey: ['prompts', promptId, 'versions', 'compare', from, to],
    queryFn: () => versionsApi.compareVersions(promptId, from!, to!),
    enabled: from !== null && to !== null,
  })
}

export function useRestoreVersion(promptId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (versionNumber: number) => versionsApi.restoreVersion(promptId, versionNumber),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [...PROMPTS_QUERY_KEY, promptId] })
      queryClient.invalidateQueries({ queryKey: ['prompts', promptId, 'versions'] })
    },
  })
}

export function useDuplicateVersion(promptId: string) {
  const navigate = useNavigate()
  return useMutation({
    mutationFn: (versionNumber: number) => versionsApi.duplicateVersion(promptId, versionNumber),
    onSuccess: (created) => navigate(`/prompts/${(created as { id: string }).id}`),
  })
}
