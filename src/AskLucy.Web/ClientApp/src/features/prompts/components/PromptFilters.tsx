import ClearIcon from '@mui/icons-material/Clear'
import SearchIcon from '@mui/icons-material/Search'
import {
  Autocomplete,
  Chip,
  IconButton,
  InputAdornment,
  MenuItem,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
} from '@mui/material'
import { useCategories, useTags } from '../hooks/usePromptMutations'
import type { PromptListView, PromptStatus } from '../api/promptsApi'
import type { PromptFiltersState } from './promptFiltersState'

interface PromptFiltersProps {
  value: PromptFiltersState
  onChange: (value: PromptFiltersState) => void
}

const VIEW_OPTIONS: { value: PromptListView; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'Favorites', label: 'Favorites' },
  { value: 'Pinned', label: 'Pinned' },
  { value: 'RecentlyUsed', label: 'Recently used' },
  { value: 'RecentlyModified', label: 'Recently modified' },
  { value: 'Archived', label: 'Archived' },
]

/** Search bar + filters panel (FR-050–FR-053, spec.md User Story 4). Folder selection lives in `FolderTree`, not here. */
export function PromptFilters({ value, onChange }: PromptFiltersProps) {
  const { data: categories = [] } = useCategories()
  const { data: tags = [] } = useTags()

  return (
    <Stack spacing={1.5} data-testid="prompt-filters">
      <TextField
        placeholder="Search prompts by name, description, content…"
        size="small"
        fullWidth
        value={value.q}
        onChange={(e) => onChange({ ...value, q: e.target.value })}
        slotProps={{
          input: {
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon fontSize="small" />
              </InputAdornment>
            ),
            endAdornment: value.q && (
              <InputAdornment position="end">
                <IconButton size="small" aria-label="Clear search" onClick={() => onChange({ ...value, q: '' })}>
                  <ClearIcon fontSize="small" />
                </IconButton>
              </InputAdornment>
            ),
          },
        }}
      />

      <ToggleButtonGroup
        exclusive
        size="small"
        value={value.view}
        onChange={(_, next: PromptListView | null) => next && onChange({ ...value, view: next })}
        sx={{ flexWrap: 'wrap' }}
      >
        {VIEW_OPTIONS.map((option) => (
          <ToggleButton key={option.value} value={option.value} data-testid={`prompt-view-${option.value}`}>
            {option.label}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>

      <Stack direction="row" spacing={1.5} sx={{ flexWrap: 'wrap' }}>
        <TextField
          select
          size="small"
          label="Category"
          value={value.categoryId ?? ''}
          onChange={(e) => onChange({ ...value, categoryId: e.target.value || null })}
          sx={{ minWidth: 180 }}
        >
          <MenuItem value="">All categories</MenuItem>
          {categories.map((category) => (
            <MenuItem key={category.id} value={category.id}>
              {category.name}
            </MenuItem>
          ))}
        </TextField>

        <Autocomplete
          size="small"
          options={tags}
          value={value.tag}
          onChange={(_, next) => onChange({ ...value, tag: next })}
          sx={{ minWidth: 180 }}
          renderInput={(params) => <TextField {...params} label="Tag" />}
        />

        <TextField
          select
          size="small"
          label="Status"
          value={value.status ?? ''}
          onChange={(e) => onChange({ ...value, status: (e.target.value || null) as PromptStatus | null })}
          sx={{ minWidth: 160 }}
        >
          <MenuItem value="">Any status</MenuItem>
          <MenuItem value="Draft">Draft</MenuItem>
          <MenuItem value="Active">Active</MenuItem>
          <MenuItem value="Archived">Archived</MenuItem>
        </TextField>
      </Stack>

      {(value.categoryId || value.tag || value.status) && (
        <Stack direction="row" spacing={1}>
          {value.categoryId && (
            <Chip
              size="small"
              label={`Category: ${categories.find((c) => c.id === value.categoryId)?.name ?? ''}`}
              onDelete={() => onChange({ ...value, categoryId: null })}
            />
          )}
          {value.tag && <Chip size="small" label={`Tag: ${value.tag}`} onDelete={() => onChange({ ...value, tag: null })} />}
          {value.status && <Chip size="small" label={`Status: ${value.status}`} onDelete={() => onChange({ ...value, status: null })} />}
        </Stack>
      )}
    </Stack>
  )
}
