import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as taxonomyApi from '../api/knowledgeBaseTaxonomyApi'
import { KNOWLEDGE_BASES_QUERY_KEY } from './useKnowledgeBases'

const CATEGORIES_QUERY_KEY = [...KNOWLEDGE_BASES_QUERY_KEY, 'categories']
const TAGS_QUERY_KEY = [...KNOWLEDGE_BASES_QUERY_KEY, 'tags']

export function useKnowledgeBaseCategories() {
  return useQuery({
    queryKey: CATEGORIES_QUERY_KEY,
    queryFn: () => taxonomyApi.listKnowledgeBaseCategories(),
  })
}

export function useCreateKnowledgeBaseCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => taxonomyApi.createKnowledgeBaseCategory(name),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CATEGORIES_QUERY_KEY }),
  })
}

/** Deleting a category also changes affected knowledge bases' `categoryId` (FR-021), so the knowledge base list is invalidated too, not just the category list. */
export function useDeleteKnowledgeBaseCategory() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => taxonomyApi.deleteKnowledgeBaseCategory(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CATEGORIES_QUERY_KEY })
      queryClient.invalidateQueries({ queryKey: KNOWLEDGE_BASES_QUERY_KEY })
    },
  })
}

export function useKnowledgeBaseTags(prefix?: string) {
  return useQuery({
    queryKey: [...TAGS_QUERY_KEY, prefix ?? ''],
    queryFn: () => taxonomyApi.listKnowledgeBaseTags(prefix),
  })
}
