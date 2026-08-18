import { Autocomplete, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material'
import { useMemo } from 'react'
import { useSearchKnowledgeBases } from '../../knowledge-base/hooks/useSearchKnowledgeBases'
import type { ExecutionContextOptionsState } from './executionContextOptionsState'

interface ExecutionContextOptionsProps {
  value: ExecutionContextOptionsState
  onChange: (value: ExecutionContextOptionsState) => void
}

/**
 * RAG/Memory execution-request options for the Testing Console (spec.md FR-081/FR-082, User
 * Story 6) — reuses the existing Knowledge Base picker data, never a duplicate retrieval
 * mechanism of its own (research.md Decision 3).
 */
export function ExecutionContextOptions({ value, onChange }: ExecutionContextOptionsProps) {
  const { data } = useSearchKnowledgeBases({ view: 'Active', pageSize: 100 })
  const knowledgeBases = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data])
  const selectedKnowledgeBases = knowledgeBases.filter((kb) => value.knowledgeBaseIds.includes(kb.id))

  return (
    <Stack spacing={1.5} data-testid="execution-context-options">
      <Typography variant="subtitle1">Context</Typography>

      <FormControlLabel
        control={
          <Checkbox
            checked={value.useRagContext}
            onChange={(e) => onChange({ ...value, useRagContext: e.target.checked })}
          />
        }
        label="Use Knowledge Base (RAG) context"
      />
      {value.useRagContext && (
        <Autocomplete
          multiple
          size="small"
          options={knowledgeBases}
          value={selectedKnowledgeBases}
          getOptionLabel={(kb) => kb.name}
          isOptionEqualToValue={(a, b) => a.id === b.id}
          onChange={(_, next) => onChange({ ...value, knowledgeBaseIds: next.map((kb) => kb.id) })}
          renderInput={(params) => <TextField {...params} label="Knowledge bases" placeholder="Select…" />}
        />
      )}

      <FormControlLabel
        control={
          <Checkbox
            checked={value.useMemoryContext}
            onChange={(e) => onChange({ ...value, useMemoryContext: e.target.checked })}
          />
        }
        label="Use Memory context"
      />
    </Stack>
  )
}
