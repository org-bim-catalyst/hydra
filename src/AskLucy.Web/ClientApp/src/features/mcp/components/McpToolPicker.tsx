import { useMemo, useState } from 'react'
import {
  Box,
  Button,
  Checkbox,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import type { AgentToolRiskLevel } from '../api/mcpServersApi'
import { useMcpCatalogTool, useMcpCatalogTools } from '../hooks/useMcpCatalog'

const RISK_COLOR: Record<AgentToolRiskLevel, 'success' | 'info' | 'warning' | 'error'> = {
  Low: 'success',
  Medium: 'info',
  High: 'warning',
  Critical: 'error',
}

interface McpToolPickerProps {
  selectedToolNames: string[]
  onChange: (toolNames: string[]) => void
}

/**
 * spec.md FR-062/User Story 4 — browse the MCP tools available to the current user (only
 * `Active`+`Available`+enabled+healthy ones, identical to what an agent could actually call) and
 * enable/disable specific ones. A search/filter box and a tool-detail dialog (description, risk,
 * required permissions, input/output schema) are shown before a tool is enabled (FR-062).
 *
 * Standalone/controlled (`selectedToolNames`/`onChange`) rather than wired into
 * `AgentBuilder.tsx` directly — spec 020's Agent Builder has no native tool-selection UI to
 * integrate into yet (only a read-only `toolNames` display), so there is no existing "tool
 * selector" for this to extend; that gap belongs to spec 020, not this feature.
 */
export function McpToolPicker({ selectedToolNames, onChange }: McpToolPickerProps) {
  const { data: tools, isLoading } = useMcpCatalogTools()
  const [search, setSearch] = useState('')
  const [detailToolName, setDetailToolName] = useState<string | null>(null)
  const { data: detail, isLoading: isDetailLoading } = useMcpCatalogTool(detailToolName)

  const filteredTools = useMemo(() => {
    const query = search.trim().toLowerCase()
    if (!query) return tools ?? []
    return (tools ?? []).filter(
      (tool) => tool.displayName.toLowerCase().includes(query) || tool.description.toLowerCase().includes(query) || tool.sourceServerName.toLowerCase().includes(query),
    )
  }, [tools, search])

  const toggle = (namespacedName: string) =>
    onChange(
      selectedToolNames.includes(namespacedName)
        ? selectedToolNames.filter((name) => name !== namespacedName)
        : [...selectedToolNames, namespacedName],
    )

  return (
    <Box>
      <TextField
        label="Search MCP tools"
        size="small"
        fullWidth
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        sx={{ mb: 2 }}
      />

      {isLoading && <Typography color="text.secondary">Loading…</Typography>}
      {!isLoading && filteredTools.length === 0 && <Typography color="text.secondary">No MCP tools available.</Typography>}

      <List dense>
        {filteredTools.map((tool) => (
          <ListItem
            key={tool.namespacedName}
            disablePadding
            sx={{ py: 0.5, pl: 2 }}
            secondaryAction={
              <Button size="small" onClick={() => setDetailToolName(tool.namespacedName)}>
                Details
              </Button>
            }
          >
            <ListItemIcon>
              <Checkbox
                edge="start"
                slotProps={{ input: { 'aria-label': `Enable ${tool.displayName} for this agent` } }}
                checked={selectedToolNames.includes(tool.namespacedName)}
                onChange={() => toggle(tool.namespacedName)}
              />
            </ListItemIcon>
            <ListItemText
              primary={
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                  <span>{tool.displayName}</span>
                  <Chip label={tool.effectiveRiskLevel} color={RISK_COLOR[tool.effectiveRiskLevel]} size="small" />
                  <Chip label={tool.sourceServerName} size="small" variant="outlined" />
                </Stack>
              }
              secondary={tool.description}
            />
          </ListItem>
        ))}
      </List>

      <Dialog open={detailToolName !== null} onClose={() => setDetailToolName(null)} maxWidth="sm" fullWidth>
        <DialogTitle>{detail?.displayName ?? 'Tool details'}</DialogTitle>
        <DialogContent>
          {isDetailLoading && <Typography color="text.secondary">Loading…</Typography>}
          {detail && (
            <Stack spacing={2}>
              <Typography>{detail.description}</Typography>
              <Stack direction="row" spacing={1}>
                <Chip label={detail.effectiveRiskLevel} color={RISK_COLOR[detail.effectiveRiskLevel]} size="small" />
                <Chip label={detail.sourceServerName} size="small" variant="outlined" />
                {detail.version && <Chip label={`v${detail.version}`} size="small" variant="outlined" />}
              </Stack>
              <Box>
                <Typography variant="subtitle2">Required permissions</Typography>
                <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', mt: 0.5 }}>
                  {detail.requiredPermissions.length === 0 && <Typography variant="body2" color="text.secondary">None</Typography>}
                  {detail.requiredPermissions.map((permission) => (
                    <Chip key={permission} label={permission} size="small" variant="outlined" />
                  ))}
                </Stack>
              </Box>
              <Box>
                <Typography variant="subtitle2">Input schema</Typography>
                <Box component="pre" sx={{ fontSize: '0.75rem', overflow: 'auto', maxHeight: 160, bgcolor: 'action.hover', p: 1, borderRadius: 1 }}>
                  {detail.inputSchemaJson}
                </Box>
              </Box>
            </Stack>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDetailToolName(null)}>Close</Button>
          {detail && (
            <Button
              variant="contained"
              color={selectedToolNames.includes(detail.namespacedName) ? 'error' : 'primary'}
              onClick={() => {
                toggle(detail.namespacedName)
                setDetailToolName(null)
              }}
            >
              {selectedToolNames.includes(detail.namespacedName) ? 'Disable for this agent' : 'Enable for this agent'}
            </Button>
          )}
        </DialogActions>
      </Dialog>
    </Box>
  )
}
