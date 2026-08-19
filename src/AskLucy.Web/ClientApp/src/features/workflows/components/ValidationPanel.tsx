import { Alert, Badge, Box, Button, List, ListItemButton, ListItemText, Stack, Typography } from '@mui/material'
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutlined'
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutlined'
import RuleOutlinedIcon from '@mui/icons-material/RuleOutlined'
import { useMemo } from 'react'
import type { WorkflowValidationIssue } from '../api/workflowsApi'
import { useWorkflowCanvasStore } from '../store/workflowCanvasStore'

interface ValidationPanelProps {
  issues: WorkflowValidationIssue[]
  hasValidated: boolean
  isValidating: boolean
  onValidate: () => void
}

/** spec.md User Story 2 — calls `POST /workflows/{id}/actions/validate`, renders violations with node location, and is the single source of truth `WorkflowDesignerPage` gates the Publish action on. */
export function ValidationPanel({ issues, hasValidated, isValidating, onValidate }: ValidationPanelProps) {
  const setSelectedNodeId = useWorkflowCanvasStore((s) => s.setSelectedNodeId)
  const nodes = useWorkflowCanvasStore((s) => s.nodes)
  // A selector allocating a new Set on every call defeats Zustand's Object.is snapshot
  // comparison — every render looks "changed," which schedules another render indefinitely
  // (found via WorkflowDesignerPage.a11y.test.tsx: "Maximum update depth exceeded").
  const nodeIds = useMemo(() => new Set(nodes.map((n) => n.id)), [nodes])

  return (
    <Box sx={{ p: 2, borderTop: 1, borderColor: 'divider' }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <RuleOutlinedIcon fontSize="small" />
          <Typography variant="subtitle2" component="span">Validation</Typography>
          {hasValidated && (
            <Badge badgeContent={issues.length} color={issues.length > 0 ? 'error' : 'success'} showZero />
          )}
        </Stack>
        <Button size="small" variant="outlined" onClick={onValidate} disabled={isValidating}>
          {isValidating ? 'Validating…' : 'Validate'}
        </Button>
      </Stack>

      {!hasValidated && (
        <Typography variant="body2" color="text.secondary">
          Run validation before publishing — a workflow with any violation cannot be published (SC-009).
        </Typography>
      )}

      {hasValidated && issues.length === 0 && (
        <Alert severity="success" icon={<CheckCircleOutlineIcon fontSize="inherit" />}>
          No violations — this draft is ready to publish.
        </Alert>
      )}

      {hasValidated && issues.length > 0 && (
        <List dense sx={{ maxHeight: 220, overflowY: 'auto' }}>
          {issues.map((issue, index) => (
            <ListItemButton
              key={index}
              disabled={!issue.nodeKey || !nodeIds.has(issue.nodeKey)}
              onClick={() => issue.nodeKey && setSelectedNodeId(issue.nodeKey)}
            >
              <ErrorOutlineIcon fontSize="small" color="error" sx={{ mr: 1 }} />
              <ListItemText primary={issue.message} secondary={issue.nodeKey ? `Node: ${issue.nodeKey}` : 'Workflow-level'} />
            </ListItemButton>
          ))}
        </List>
      )}
    </Box>
  )
}
