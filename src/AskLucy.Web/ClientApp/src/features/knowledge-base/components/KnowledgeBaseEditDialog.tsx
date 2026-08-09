import { Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField } from '@mui/material'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import type { KnowledgeBaseSummary } from '../api/knowledgeBasesApi'
import { KnowledgeBaseCategoryManagerDialog } from './KnowledgeBaseCategoryManagerDialog'
import { useCreateKnowledgeBaseCategory, useKnowledgeBaseCategories } from '../hooks/useKnowledgeBaseTaxonomy'

export interface KnowledgeBaseFormValues {
  name: string
  description: string
  color: string
  icon: string
  categoryId: string | null
  tags: string[]
}

interface KnowledgeBaseEditDialogProps {
  open: boolean
  /** Present when editing an existing knowledge base; absent when creating a new one. */
  knowledgeBase?: KnowledgeBaseSummary
  submitting: boolean
  errorMessage: string | null
  onSubmit: (values: KnowledgeBaseFormValues) => void
  onClose: () => void
}

const CREATE_CATEGORY_OPTION = '__create__'
const MANAGE_CATEGORIES_OPTION = '__manage__'

/**
 * Shared create/edit form (FR-001/FR-003) — a single dialog rather than two near-identical
 * ones, since the fields and validation are the same; only the submit label and initial
 * values differ. Category/tag pickers (US5, FR-017–FR-020) are plain field-level state, not
 * `react-hook-form`-registered, since a `<select>`'s special "create new"/"manage" sentinel
 * values and a chip-based tag editor don't map cleanly onto RHF's `register()` — they're
 * merged into the submitted `KnowledgeBaseFormValues` alongside the registered fields. The
 * parent remounts this component (via a `key` keyed on the knowledge base id/"create") each
 * time a different knowledge base is opened for editing, so `categoryId`/`tags` only need a
 * plain `useState` initializer, not an effect resyncing them on prop change.
 */
export function KnowledgeBaseEditDialog({ open, knowledgeBase, submitting, errorMessage, onSubmit, onClose }: KnowledgeBaseEditDialogProps) {
  const { register, handleSubmit, formState } = useForm<Omit<KnowledgeBaseFormValues, 'categoryId' | 'tags'>>({
    values: {
      name: knowledgeBase?.name ?? '',
      description: knowledgeBase?.description ?? '',
      color: knowledgeBase?.color ?? '',
      icon: knowledgeBase?.icon ?? '',
    },
  })

  const { data: categories } = useKnowledgeBaseCategories()
  const createCategory = useCreateKnowledgeBaseCategory()

  const [categoryId, setCategoryId] = useState<string | null>(knowledgeBase?.categoryId ?? null)
  const [tags, setTags] = useState<string[]>(knowledgeBase?.tags ?? [])
  const [tagInput, setTagInput] = useState('')
  const [creatingCategory, setCreatingCategory] = useState(false)
  const [newCategoryName, setNewCategoryName] = useState('')
  const [categoryError, setCategoryError] = useState<string | null>(null)
  const [managerOpen, setManagerOpen] = useState(false)

  const isEditing = knowledgeBase !== undefined

  const addTag = () => {
    const value = tagInput.trim()
    if (value && !tags.includes(value)) {
      setTags([...tags, value])
    }
    setTagInput('')
  }

  const handleCreateCategory = () => {
    const name = newCategoryName.trim()
    if (!name) return
    createCategory.mutate(name, {
      onSuccess: (category) => {
        setCategoryId(category.id)
        setCreatingCategory(false)
        setNewCategoryName('')
        setCategoryError(null)
      },
      onError: (err) => setCategoryError(err instanceof Error ? err.message : 'Could not create category. Please try again.'),
    })
  }

  const submit = handleSubmit((values) => onSubmit({ ...values, categoryId, tags }))

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth aria-labelledby="knowledge-base-dialog-title">
      <DialogTitle id="knowledge-base-dialog-title">{isEditing ? 'Edit knowledge base' : 'New Knowledge Base'}</DialogTitle>
      <Box component="form" onSubmit={submit}>
        <DialogContent>
          <Stack spacing={2.5}>
            <TextField
              label="Name"
              fullWidth
              autoFocus
              required
              {...register('name', { required: 'A knowledge base name is required.' })}
              error={Boolean(formState.errors.name)}
              helperText={formState.errors.name?.message}
            />
            <TextField label="Description" fullWidth multiline rows={3} {...register('description')} />
            <TextField label="Color" placeholder="#4F46E5" fullWidth {...register('color')} />
            <TextField label="Icon" placeholder="folder-open" fullWidth {...register('icon')} />

            <TextField
              select
              label="Category"
              aria-label="Category"
              fullWidth
              value={categoryId ?? ''}
              onChange={(e) => {
                if (e.target.value === CREATE_CATEGORY_OPTION) {
                  setCreatingCategory(true)
                  return
                }
                if (e.target.value === MANAGE_CATEGORIES_OPTION) {
                  setManagerOpen(true)
                  return
                }
                setCategoryId(e.target.value || null)
              }}
            >
              <MenuItem value="">Uncategorized</MenuItem>
              {(categories ?? []).map((category) => (
                <MenuItem key={category.id} value={category.id}>
                  {category.name}
                </MenuItem>
              ))}
              <MenuItem value={CREATE_CATEGORY_OPTION}>Create new category…</MenuItem>
              <MenuItem value={MANAGE_CATEGORIES_OPTION}>Manage categories…</MenuItem>
            </TextField>

            {creatingCategory && (
              <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
                <TextField
                  label="New category name"
                  aria-label="New category name"
                  size="small"
                  fullWidth
                  autoFocus
                  value={newCategoryName}
                  onChange={(e) => setNewCategoryName(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault()
                      handleCreateCategory()
                    }
                  }}
                  error={Boolean(categoryError)}
                  helperText={categoryError}
                />
                <Button onClick={handleCreateCategory} disabled={createCategory.isPending}>
                  Create
                </Button>
              </Stack>
            )}

            <TextField
              label="Tags"
              aria-label="Tags"
              fullWidth
              placeholder="Type a tag and press Enter"
              helperText="Press Enter to add a tag"
              value={tagInput}
              onChange={(e) => setTagInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault()
                  addTag()
                }
              }}
            />
            {tags.length > 0 && (
              <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', gap: 0.5 }}>
                {tags.map((tag) => (
                  <Chip key={tag} label={tag} size="small" onDelete={() => setTags(tags.filter((t) => t !== tag))} />
                ))}
              </Stack>
            )}

            {errorMessage && <Alert severity="error">{errorMessage}</Alert>}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={submitting}>
            {isEditing ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </Box>

      <KnowledgeBaseCategoryManagerDialog open={managerOpen} onClose={() => setManagerOpen(false)} />
    </Dialog>
  )
}
