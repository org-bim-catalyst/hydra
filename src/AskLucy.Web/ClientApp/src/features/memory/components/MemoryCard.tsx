import DeleteIcon from '@mui/icons-material/Delete'
import EditIcon from '@mui/icons-material/Edit'
import LockIcon from '@mui/icons-material/Lock'
import { Card, CardContent, Chip, IconButton, Stack, Typography } from '@mui/material'
import type { MemoryListItem } from '../api/memoryApi'

const CATEGORY_LABEL: Record<MemoryListItem['category'], string> = {
  UserPreference: 'Preference',
  PersonalFact: 'Personal fact',
  ProjectContext: 'Project context',
  ConversationDerived: 'From conversation',
}

const STATE_COLOR: Record<MemoryListItem['state'], 'default' | 'success' | 'warning'> = {
  PendingApproval: 'warning',
  Active: 'success',
  Archived: 'default',
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

interface MemoryCardProps {
  memory: MemoryListItem
  onEdit: () => void
  onDelete: () => void
}

/** A single memory's Memory Center tile (spec.md FR-017, User Story 2 AC1) — shows exactly the fields FR-017 lists: content, category, source, creation date, and lifecycle state. State is a text `Chip`, not a bare color swatch — color alone must never be the only signal. */
export function MemoryCard({ memory, onEdit, onDelete }: MemoryCardProps) {
  return (
    <Card data-testid="memory-card" variant="outlined">
      <CardContent>
        <Stack direction="row" sx={{ alignItems: 'flex-start', justifyContent: 'space-between' }}>
          <Typography variant="body1" sx={{ flex: 1, mr: 1 }}>
            {memory.content}
          </Typography>
          <Stack direction="row">
            <IconButton size="small" aria-label="Edit memory" onClick={onEdit}>
              <EditIcon fontSize="small" />
            </IconButton>
            <IconButton size="small" aria-label="Delete memory" onClick={onDelete}>
              <DeleteIcon fontSize="small" />
            </IconButton>
          </Stack>
        </Stack>

        <Stack direction="row" spacing={1} sx={{ mt: 1.5, alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
          <Chip size="small" variant="outlined" label={CATEGORY_LABEL[memory.category]} />
          <Chip size="small" label={memory.state} color={STATE_COLOR[memory.state]} data-testid="memory-state" />
          {memory.isSensitive && <Chip size="small" icon={<LockIcon fontSize="small" />} label="Sensitive" color="error" variant="outlined" />}
          {memory.projectName && <Chip size="small" variant="outlined" label={memory.projectName} />}
          <Typography variant="caption" color="text.secondary">
            {formatDate(memory.createdAtUtc)}
          </Typography>
        </Stack>
      </CardContent>
    </Card>
  )
}
