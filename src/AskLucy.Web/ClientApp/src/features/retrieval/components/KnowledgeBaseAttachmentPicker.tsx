import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Autocomplete, Chip, TextField } from '@mui/material'
import { searchKnowledgeBases, type KnowledgeBaseSummary } from '../../knowledge-base/api/knowledgeBasesApi'
import { useConversationKnowledgeBases } from '../hooks/useConversationKnowledgeBases'

/**
 * specs/016-rag-semantic-search US1 T055 — attach/detach knowledge bases on a conversation.
 * A multi-select Autocomplete over the caller's active knowledge bases, full-replacing the
 * attached set on every change (matches `PUT /api/v1/chats/{id}/knowledge-bases`'s contract).
 */
export function KnowledgeBaseAttachmentPicker({ chatId }: { chatId: string | null }) {
  const [error, setError] = useState<string | null>(null)

  const { data } = useQuery({
    queryKey: ['knowledge-bases', 'attachment-picker-options'],
    queryFn: () => searchKnowledgeBases({ view: 'Active', pageSize: 100 }),
  })
  const options = data?.items ?? []

  const { knowledgeBaseIds, isLoading, updateKnowledgeBases, isUpdating } = useConversationKnowledgeBases(chatId)
  const selected = options.filter((kb) => knowledgeBaseIds.includes(kb.id))

  const handleChange = async (next: KnowledgeBaseSummary[]) => {
    if (!chatId) {
      setError('Start the conversation before attaching a knowledge base.')
      return
    }

    try {
      await updateKnowledgeBases(next.map((kb) => kb.id))
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update the attached knowledge bases.')
    }
  }

  return (
    <Autocomplete<KnowledgeBaseSummary, true, false, false>
      multiple
      size="small"
      options={options}
      value={selected}
      loading={isLoading}
      disabled={isUpdating}
      getOptionLabel={(kb) => kb.name}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      onChange={(_event, next) => void handleChange(next)}
      renderValue={(value, getItemProps) =>
        value.map((kb, index) => <Chip size="small" label={kb.name} {...getItemProps({ index })} key={kb.id} />)
      }
      renderInput={(params) => (
        <TextField
          {...params}
          label="Knowledge bases"
          placeholder={selected.length === 0 ? 'None attached' : undefined}
          error={Boolean(error)}
          helperText={error}
        />
      )}
      sx={{ minWidth: 260 }}
    />
  )
}
