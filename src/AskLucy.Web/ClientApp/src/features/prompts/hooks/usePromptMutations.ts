import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as promptsApi from '../api/promptsApi'
import type { SavePromptInput, UpdatePromptInput } from '../api/promptsApi'
import { PROMPTS_QUERY_KEY } from './usePrompts'

export function useCreatePrompt() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: SavePromptInput) => promptsApi.createPrompt(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY }),
  })
}

export function useUpdatePrompt() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdatePromptInput }) => promptsApi.updatePrompt(id, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY }),
  })
}

/** Soft delete — no confirmation required at this layer (FR-001); the caller's UI decides whether to confirm. */
export function useDeletePrompt() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => promptsApi.deletePrompt(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY }),
  })
}

export function useArchivePrompt() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => promptsApi.archivePrompt(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY }),
  })
}

export function useRestorePrompt() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => promptsApi.restorePrompt(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY }),
  })
}

/** Deep copy — new prompt, fresh version-1 history, auto-suffixed name on collision (FR-001, FR-006). */
export function useDuplicatePrompt() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => promptsApi.duplicatePrompt(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY }),
  })
}

/** Resolves content with supplied/example/default variable values — no AI provider call (FR-005). */
export function usePreviewPrompt() {
  return useMutation({
    mutationFn: ({ id, variableValues }: { id: string; variableValues: Record<string, string | null> }) =>
      promptsApi.previewPrompt(id, variableValues),
  })
}

const PROMPTS_LIST_QUERY_KEY = ['prompts', 'list']

export function useSetFavorite() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, isFavorite }: { id: string; isFavorite: boolean }) => promptsApi.setFavorite(id, isFavorite),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_LIST_QUERY_KEY }),
  })
}

export function useSetPinned() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, isPinned }: { id: string; isPinned: boolean }) => promptsApi.setPinned(id, isPinned),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_LIST_QUERY_KEY }),
  })
}

export function useCategories() {
  return useQuery({ queryKey: ['prompt-categories'], queryFn: () => promptsApi.listCategories() })
}

export function useCreateCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => promptsApi.createCategory(name),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['prompt-categories'] }),
  })
}

export function useTags() {
  return useQuery({ queryKey: ['prompt-tags'], queryFn: () => promptsApi.listTags() })
}

export function useAddTag() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, value }: { id: string; value: string }) => promptsApi.addTag(id, value),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY })
      queryClient.invalidateQueries({ queryKey: ['prompt-tags'] })
    },
  })
}

export function useRemoveTag() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, tagId }: { id: string; tagId: string }) => promptsApi.removeTag(id, tagId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY }),
  })
}

export function useExportPrompts() {
  return useMutation({
    mutationFn: (promptIds: string[]) => promptsApi.exportPrompts(promptIds),
  })
}

export function useImportPrompts() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (file: promptsApi.PromptExportFile) => promptsApi.importPrompts(file),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY }),
  })
}
