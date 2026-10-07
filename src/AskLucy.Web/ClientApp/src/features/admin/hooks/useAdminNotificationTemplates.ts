import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as api from '../api/adminNotificationTemplatesApi'
import type {
  TemplateListFilters,
  TemplateVersionInput,
} from '../api/adminNotificationTemplatesApi'

const KEY = api.ADMIN_TEMPLATES_QUERY_KEY

export function useNotificationTemplates(filters: TemplateListFilters) {
  return useQuery({
    queryKey: [...KEY, 'list', filters],
    queryFn: () => api.getNotificationTemplates(filters),
    placeholderData: keepPreviousData,
  })
}

export function useNotificationTemplate(templateId: string) {
  return useQuery({
    queryKey: [...KEY, 'detail', templateId],
    queryFn: () => api.getNotificationTemplate(templateId),
  })
}

export function useTemplateVersion(templateId: string, versionId: string | null) {
  return useQuery({
    queryKey: [...KEY, 'version', templateId, versionId],
    queryFn: () => api.getTemplateVersion(templateId, versionId!),
    enabled: versionId !== null,
  })
}

/** Every change of a version also changes the list (published/draft badges) and the detail's version list, so the whole area refetches. */
function useTemplateMutation<TInput, TResult>(fn: (input: TInput) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export const useCreateTemplateDraft = (templateId: string) =>
  useTemplateMutation((input: TemplateVersionInput) => api.createTemplateDraft(templateId, input))

export const useUpdateTemplateDraft = (templateId: string) =>
  useTemplateMutation(
    (input: { versionId: string; rowVersion: string; content: TemplateVersionInput }) =>
      api.updateTemplateDraft(templateId, input.versionId, input.rowVersion, input.content),
  )

export const usePublishTemplateVersion = (templateId: string) =>
  useTemplateMutation((input: { versionId: string; rowVersion: string }) =>
    api.publishTemplateVersion(templateId, input.versionId, input.rowVersion),
  )

export const useArchiveTemplateVersion = (templateId: string) =>
  useTemplateMutation((input: { versionId: string; rowVersion: string }) =>
    api.archiveTemplateVersion(templateId, input.versionId, input.rowVersion),
  )

/** A preview or a test send changes nothing stored, so neither refetches anything. */
export const usePreviewTemplateVersion = (templateId: string) =>
  useMutation({
    mutationFn: (versionId: string) => api.previewTemplateVersion(templateId, versionId),
  })

export const useSendTemplateTest = (templateId: string) =>
  useMutation({ mutationFn: (versionId: string) => api.sendTemplateTest(templateId, versionId) })
