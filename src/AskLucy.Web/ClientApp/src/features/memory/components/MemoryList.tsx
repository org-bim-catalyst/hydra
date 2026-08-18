import PsychologyOutlinedIcon from '@mui/icons-material/PsychologyOutlined'
import { Box, Button } from '@mui/material'
import { EmptyState } from '../../../components/EmptyState'
import type { MemoryListItem } from '../api/memoryApi'
import { MemoryCard } from './MemoryCard'

interface MemoryListProps {
  memories: MemoryListItem[]
  isLoading: boolean
  isFiltered: boolean
  hasNextPage: boolean | undefined
  isFetchingNextPage: boolean
  onFetchNextPage: () => void
  onEdit: (memory: MemoryListItem) => void
  onDelete: (memory: MemoryListItem) => void
}

/** The Memory Center's results list (spec.md FR-017, User Story 2 AC1) — cursor-paginated via a "Load more" button, mirroring `KnowledgeBaseDashboardPage`'s identical pattern. */
export function MemoryList({ memories, isLoading, isFiltered, hasNextPage, isFetchingNextPage, onFetchNextPage, onEdit, onDelete }: MemoryListProps) {
  if (!isLoading && memories.length === 0) {
    return (
      <EmptyState
        icon={<PsychologyOutlinedIcon fontSize="inherit" />}
        title={isFiltered ? 'No matching memories' : 'Nothing remembered yet'}
        description={
          isFiltered
            ? 'Try a different search term or filter.'
            : 'As you chat with Lucy, facts and preferences she picks up on will show up here.'
        }
      />
    )
  }

  return (
    <>
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
        {memories.map((memory) => (
          <MemoryCard key={memory.id} memory={memory} onEdit={() => onEdit(memory)} onDelete={() => onDelete(memory)} />
        ))}
      </Box>

      {hasNextPage && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
          <Button variant="outlined" onClick={onFetchNextPage} loading={isFetchingNextPage}>
            Load more
          </Button>
        </Box>
      )}
    </>
  )
}
