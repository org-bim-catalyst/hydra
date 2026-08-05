import { MenuItem, Stack, TextField } from '@mui/material'
import type { DocumentProcessingStatus, DocumentSearchFilters } from '../api/documentsApi'
import { useCategories, useTags } from '../hooks/useDocuments'

interface DocumentFilterBarProps {
  filters: DocumentSearchFilters
  onChange: (filters: DocumentSearchFilters) => void
}

const statuses: DocumentProcessingStatus[] = ['Uploaded', 'Queued', 'Processing', 'Completed', 'Failed']

/** FR-035–FR-037 — combined search/filter bar (filename/metadata text, author, language, tag, category, date range, status). */
export function DocumentFilterBar({ filters, onChange }: DocumentFilterBarProps) {
  const { data: categories } = useCategories()
  const { data: tags } = useTags()

  const set = <K extends keyof DocumentSearchFilters>(key: K, value: DocumentSearchFilters[K]) =>
    onChange({ ...filters, [key]: value || undefined })

  return (
    <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1, mb: 2 }}>
      <TextField
        size="small"
        label="Search"
        value={filters.q ?? ''}
        onChange={(e) => set('q', e.target.value)}
        sx={{ minWidth: 180 }}
      />
      <TextField size="small" label="Author" value={filters.author ?? ''} onChange={(e) => set('author', e.target.value)} sx={{ minWidth: 140 }} />
      <TextField
        select
        size="small"
        label="Tag"
        value={filters.tag ?? ''}
        onChange={(e) => set('tag', e.target.value)}
        sx={{ minWidth: 140 }}
      >
        <MenuItem value="">Any</MenuItem>
        {tags?.map((tag) => (
          <MenuItem key={tag} value={tag}>
            {tag}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        select
        size="small"
        label="Category"
        value={filters.categoryId ?? ''}
        onChange={(e) => set('categoryId', e.target.value)}
        sx={{ minWidth: 160 }}
      >
        <MenuItem value="">Any</MenuItem>
        {categories?.map((category) => (
          <MenuItem key={category.id} value={category.id}>
            {category.name}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        select
        size="small"
        label="Status"
        value={filters.status ?? ''}
        onChange={(e) => set('status', e.target.value as DocumentProcessingStatus)}
        sx={{ minWidth: 140 }}
      >
        <MenuItem value="">Any</MenuItem>
        {statuses.map((status) => (
          <MenuItem key={status} value={status}>
            {status}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        size="small"
        type="date"
        label="From"
        slotProps={{ inputLabel: { shrink: true } }}
        value={filters.dateFrom?.slice(0, 10) ?? ''}
        onChange={(e) => set('dateFrom', e.target.value ? new Date(e.target.value).toISOString() : undefined)}
      />
      <TextField
        size="small"
        type="date"
        label="To"
        slotProps={{ inputLabel: { shrink: true } }}
        value={filters.dateTo?.slice(0, 10) ?? ''}
        onChange={(e) => set('dateTo', e.target.value ? new Date(e.target.value).toISOString() : undefined)}
      />
    </Stack>
  )
}
