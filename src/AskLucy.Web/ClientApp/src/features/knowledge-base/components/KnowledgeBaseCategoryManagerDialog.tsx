import DeleteIcon from '@mui/icons-material/Delete'
import { Alert, Dialog, DialogContent, DialogTitle, IconButton, List, ListItem, ListItemText, Typography } from '@mui/material'
import { useState } from 'react'
import { useDeleteKnowledgeBaseCategory, useKnowledgeBaseCategories } from '../hooks/useKnowledgeBaseTaxonomy'

interface KnowledgeBaseCategoryManagerDialogProps {
  open: boolean
  onClose: () => void
}

/** Lists the caller's own custom categories with a delete action (FR-021) — predefined categories aren't shown here since they can never be deleted. */
export function KnowledgeBaseCategoryManagerDialog({ open, onClose }: KnowledgeBaseCategoryManagerDialogProps) {
  const { data: categories } = useKnowledgeBaseCategories()
  const deleteCategory = useDeleteKnowledgeBaseCategory()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const customCategories = (categories ?? []).filter((c) => !c.isPredefined)

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth aria-labelledby="manage-categories-title">
      <DialogTitle id="manage-categories-title">Manage categories</DialogTitle>
      <DialogContent>
        {errorMessage && (
          <Alert severity="error" sx={{ mb: 2 }} onClose={() => setErrorMessage(null)}>
            {errorMessage}
          </Alert>
        )}
        {customCategories.length === 0 ? (
          <Typography color="text.secondary">You haven't created any custom categories yet.</Typography>
        ) : (
          <List role="list" dense>
            {customCategories.map((category) => (
              <ListItem
                key={category.id}
                role="listitem"
                aria-label={category.name}
                secondaryAction={
                  <IconButton
                    edge="end"
                    aria-label="Delete category"
                    onClick={() =>
                      deleteCategory.mutate(category.id, {
                        onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Delete failed. Please try again.'),
                      })
                    }
                  >
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                }
              >
                <ListItemText primary={category.name} />
              </ListItem>
            ))}
          </List>
        )}
      </DialogContent>
    </Dialog>
  )
}
