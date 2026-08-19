import { useState } from 'react'
import { Alert, Box, Button, Chip, List, ListItem, ListItemText, Stack, Tooltip, Typography } from '@mui/material'
import ContentCopyIcon from '@mui/icons-material/ContentCopy'
import { useMcpCatalogPrompts, useMcpCatalogResources, useDuplicateMcpPrompt } from '../hooks/useMcpCatalog'

/**
 * spec.md User Story 5 — browse the MCP resources and prompts available to the current user, and
 * duplicate a prompt into an independent, editable native copy (FR-041-FR-044).
 *
 * Standalone rather than merged into a "unified prompt picker" — spec 019's Prompt Library has no
 * such picker component to extend (only a full browse/edit workspace, `PromptLibraryPage.tsx`);
 * that gap belongs to spec 019, not this feature. Once duplicated, the new prompt already appears
 * in the existing Prompt Library like any other prompt (`PROMPTS_QUERY_KEY` invalidation).
 */
export function McpResourcesAndPromptsPanel() {
  const { data: resources, isLoading: isLoadingResources } = useMcpCatalogResources()
  const { data: prompts, isLoading: isLoadingPrompts } = useMcpCatalogPrompts()
  const duplicatePrompt = useDuplicateMcpPrompt()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [duplicatedNames, setDuplicatedNames] = useState<Set<string>>(new Set())

  const handleDuplicate = (namespacedName: string) => {
    duplicatePrompt.mutate(namespacedName, {
      onSuccess: () => setDuplicatedNames((prev) => new Set(prev).add(namespacedName)),
      onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not duplicate the prompt. Please try again.'),
    })
  }

  return (
    <Stack spacing={3}>
      {errorMessage && (
        <Alert severity="error" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      )}

      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          MCP Resources
        </Typography>
        {isLoadingResources && <Typography color="text.secondary">Loading…</Typography>}
        {!isLoadingResources && (resources ?? []).length === 0 && <Typography color="text.secondary">No MCP resources available.</Typography>}
        <List dense>
          {(resources ?? []).map((resource) => (
            <ListItem key={resource.namespacedName}>
              <ListItemText
                primary={
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <span>{resource.name}</span>
                    <Chip label={resource.sourceServerName} size="small" variant="outlined" />
                    {resource.contentType && <Chip label={resource.contentType} size="small" />}
                  </Stack>
                }
                secondary={resource.description}
              />
            </ListItem>
          ))}
        </List>
      </Box>

      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          MCP Prompts
        </Typography>
        {isLoadingPrompts && <Typography color="text.secondary">Loading…</Typography>}
        {!isLoadingPrompts && (prompts ?? []).length === 0 && <Typography color="text.secondary">No MCP prompts available.</Typography>}
        <List dense>
          {(prompts ?? []).map((prompt) => (
            <ListItem
              key={prompt.namespacedName}
              secondaryAction={
                <Tooltip title="Duplicate into an independent, editable prompt">
                  <span>
                    <Button
                      size="small"
                      startIcon={<ContentCopyIcon />}
                      disabled={duplicatePrompt.isPending || duplicatedNames.has(prompt.namespacedName)}
                      onClick={() => handleDuplicate(prompt.namespacedName)}
                    >
                      {duplicatedNames.has(prompt.namespacedName) ? 'Duplicated' : 'Duplicate'}
                    </Button>
                  </span>
                </Tooltip>
              }
            >
              <ListItemText
                primary={
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <span>{prompt.name}</span>
                    <Chip label={prompt.sourceServerName} size="small" variant="outlined" />
                  </Stack>
                }
                secondary={prompt.description}
              />
            </ListItem>
          ))}
        </List>
      </Box>
    </Stack>
  )
}
