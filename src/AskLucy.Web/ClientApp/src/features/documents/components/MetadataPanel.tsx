import AddIcon from '@mui/icons-material/Add'
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Chip,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import type { DocumentDetail } from '../api/documentsApi'
import { useCategories, useTags } from '../hooks/useDocuments'
import { useAddTag, useOverrideClassification, useRemoveTag, useUpdateDocumentMetadata } from '../hooks/useDocumentMutations'

function toDateInputValue(iso: string | null): string {
  return iso ? iso.slice(0, 10) : ''
}

interface MetadataPanelProps {
  documentId: string
  document: DocumentDetail
}

/** FR-023, FR-026, FR-031, FR-031a, FR-032 — editable extracted metadata, classification override, and tags (US3). */
export function MetadataPanel({ documentId, document }: MetadataPanelProps) {
  const { metadata, classification } = document
  const { data: categories } = useCategories()
  const { data: knownTags } = useTags()
  const updateMetadata = useUpdateDocumentMetadata()
  const overrideClassification = useOverrideClassification()
  const addTag = useAddTag()
  const removeTag = useRemoveTag()

  const [title, setTitle] = useState(metadata?.title ?? '')
  const [author, setAuthor] = useState(metadata?.author ?? '')
  const [creationDate, setCreationDate] = useState(toDateInputValue(metadata?.creationDate ?? null))
  const [keywords, setKeywords] = useState(metadata?.keywords ?? '')
  const [tagInput, setTagInput] = useState('')
  const [staleWarning, setStaleWarning] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const handleError = (err: unknown) => setErrorMessage(err instanceof Error ? err.message : 'Action failed. Please try again.')

  if (!metadata) {
    return (
      <Typography variant="body2" color="text.secondary">
        Metadata isn't available yet — this document may still be processing.
      </Typography>
    )
  }

  const handleSave = () => {
    setStaleWarning(false)
    updateMetadata.mutate(
      {
        id: documentId,
        input: {
          rowVersion: metadata.rowVersion,
          title: title.trim() || null,
          author: author.trim() || null,
          creationDate: creationDate ? new Date(creationDate).toISOString() : null,
          modificationDate: metadata.modificationDate,
          keywords: keywords.trim() || null,
        },
      },
      {
        onSuccess: (result) => setStaleWarning(result.wasStale),
        onError: handleError,
      },
    )
  }

  const handleAddTag = () => {
    const name = tagInput.trim()
    if (!name) return
    addTag.mutate({ id: documentId, name }, { onError: handleError })
    setTagInput('')
  }

  return (
    <Box>
      <Stack direction="row" sx={{ alignItems: 'center', mb: 1.5 }} spacing={1}>
        <Typography variant="subtitle2">Metadata</Typography>
        <Chip size="small" label={metadata.isAutoExtracted ? 'Auto-extracted' : 'Edited'} color={metadata.isAutoExtracted ? 'default' : 'info'} />
      </Stack>

      {staleWarning && (
        <Alert severity="warning" sx={{ mb: 1.5 }} onClose={() => setStaleWarning(false)}>
          Your view was out of date — the save succeeded, but someone else edited this document first. The page has refreshed with the latest values.
        </Alert>
      )}
      {errorMessage && (
        <Alert severity="error" sx={{ mb: 1.5 }} onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      )}

      <Stack spacing={1.5} sx={{ mb: 2 }}>
        <TextField label="Title" size="small" fullWidth value={title} onChange={(e) => setTitle(e.target.value)} />
        <TextField label="Author" size="small" fullWidth value={author} onChange={(e) => setAuthor(e.target.value)} />
        <TextField
          label="Creation date"
          type="date"
          size="small"
          fullWidth
          slotProps={{ inputLabel: { shrink: true } }}
          value={creationDate}
          onChange={(e) => setCreationDate(e.target.value)}
        />
        <TextField label="Keywords" size="small" fullWidth value={keywords} onChange={(e) => setKeywords(e.target.value)} />
        <Button size="small" variant="contained" onClick={handleSave} disabled={updateMetadata.isPending} sx={{ alignSelf: 'flex-start' }}>
          Save metadata
        </Button>
      </Stack>

      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        Classification
      </Typography>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 2 }}>
        <TextField
          select
          size="small"
          label="Category"
          sx={{ minWidth: 200 }}
          value={classification?.categoryId ?? ''}
          onChange={(e) => overrideClassification.mutate({ id: documentId, categoryId: e.target.value }, { onError: handleError })}
        >
          {categories?.map((category) => (
            <MenuItem key={category.id} value={category.id}>
              {category.name}
            </MenuItem>
          ))}
        </TextField>
        {classification && (
          <Chip size="small" label={classification.source === 'UserOverride' ? 'User override' : 'Automatic'} color={classification.source === 'UserOverride' ? 'info' : 'default'} />
        )}
      </Stack>

      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        Tags
      </Typography>
      <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', mb: 1, rowGap: 0.5 }}>
        {document.summary.tags.map((name) => (
          <Chip
            key={name}
            size="small"
            label={name}
            onDelete={() => removeTag.mutate({ id: documentId, name }, { onError: handleError })}
          />
        ))}
      </Stack>
      <Stack direction="row" spacing={1}>
        <Autocomplete
          freeSolo
          size="small"
          options={knownTags ?? []}
          inputValue={tagInput}
          onInputChange={(_, value) => setTagInput(value)}
          sx={{ flexGrow: 1 }}
          renderInput={(params) => <TextField {...params} label="Add a tag" />}
        />
        <Button size="small" startIcon={<AddIcon fontSize="small" />} onClick={handleAddTag} disabled={addTag.isPending}>
          Add
        </Button>
      </Stack>
    </Box>
  )
}
