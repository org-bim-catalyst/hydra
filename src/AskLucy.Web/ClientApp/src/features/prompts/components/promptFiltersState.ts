import type { PromptListView, PromptStatus } from '../api/promptsApi'

export interface PromptFiltersState {
  view: PromptListView
  q: string
  categoryId: string | null
  tag: string | null
  status: PromptStatus | null
}

export const DEFAULT_PROMPT_FILTERS: PromptFiltersState = {
  view: 'All',
  q: '',
  categoryId: null,
  tag: null,
  status: null,
}
