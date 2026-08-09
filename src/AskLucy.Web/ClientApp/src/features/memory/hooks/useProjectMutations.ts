import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as projectsApi from '../api/projectsApi'
import { PROJECTS_QUERY_KEY } from './useProjects'

export function useCreateProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => projectsApi.createProject(name),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROJECTS_QUERY_KEY }),
  })
}

export function useRenameProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) => projectsApi.renameProject(id, name),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROJECTS_QUERY_KEY }),
  })
}

export function useDeleteProject() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => projectsApi.deleteProject(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROJECTS_QUERY_KEY }),
  })
}

export function useAssignChatToProject() {
  return useMutation({
    mutationFn: ({ chatId, projectId }: { chatId: string; projectId: string | null }) =>
      projectsApi.assignChatToProject(chatId, projectId),
  })
}
