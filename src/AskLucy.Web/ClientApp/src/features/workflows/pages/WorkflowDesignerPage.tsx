import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import ArchiveIcon from '@mui/icons-material/Archive'
import BlockIcon from '@mui/icons-material/Block'
import ContentCopyIcon from '@mui/icons-material/ContentCopy'
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined'
import EventAvailableIcon from '@mui/icons-material/EventAvailable'
import EventBusyIcon from '@mui/icons-material/EventBusy'
import EventNoteIcon from '@mui/icons-material/EventNote'
import HistoryIcon from '@mui/icons-material/History'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import PlayArrowIcon from '@mui/icons-material/PlayArrow'
import PublishIcon from '@mui/icons-material/Publish'
import SaveIcon from '@mui/icons-material/Save'
import UnarchiveIcon from '@mui/icons-material/Unarchive'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import type { WorkflowValidationIssue } from '../api/workflowsApi'
import { EventTriggerConfigPanel } from '../components/EventTriggerConfigPanel'
import { ExecutionHistoryList } from '../components/ExecutionHistoryList'
import { NodeConfigPanel } from '../components/NodeConfigPanel'
import { NodePalette } from '../components/NodePalette'
import { ValidationPanel } from '../components/ValidationPanel'
import { VersionHistory } from '../components/VersionHistory'
import { WorkflowCanvas } from '../components/WorkflowCanvas'
import { useStartWorkflowExecution } from '../hooks/useWorkflowExecution'
import {
  useArchiveWorkflow,
  useDeleteWorkflow,
  useDeprecateWorkflow,
  useDisableWorkflow,
  useDuplicateWorkflow,
  useEnableWorkflow,
  usePublishWorkflowVersion,
  useRestoreWorkflow,
  useUpdateWorkflow,
  useValidateWorkflow,
} from '../hooks/useWorkflowMutations'
import { useWorkflow } from '../hooks/useWorkflows'
import { useWorkflowCanvasStore } from '../store/workflowCanvasStore'
import { parseDraftDefinition } from '../workflowDraftDefinition'

/**
 * spec.md User Story 2 — composes the canvas/palette/config-panel/validation-panel, wires the
 * unsaved-changes indicator to the `UpdateWorkflowCommand` save-draft flow, and gates Publish on a
 * fresh, zero-violation validation run (re-run on every Publish click rather than trusting a
 * possibly-stale prior result, since FR-016 requires the workflow to never publish with a known
 * critical violation).
 */
export function WorkflowDesignerPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: workflow, isLoading } = useWorkflow(id ?? null)

  const loadDefinition = useWorkflowCanvasStore((s) => s.loadDefinition)
  const toDraftDefinition = useWorkflowCanvasStore((s) => s.toDraftDefinition)
  const markSaved = useWorkflowCanvasStore((s) => s.markSaved)
  const reset = useWorkflowCanvasStore((s) => s.reset)
  const isDirty = useWorkflowCanvasStore((s) => s.isDirty)

  const updateWorkflow = useUpdateWorkflow()
  const validateWorkflow = useValidateWorkflow()
  const publishWorkflowVersion = usePublishWorkflowVersion()
  const duplicateWorkflow = useDuplicateWorkflow()
  const archiveWorkflow = useArchiveWorkflow()
  const restoreWorkflow = useRestoreWorkflow()
  const deleteWorkflow = useDeleteWorkflow()
  const disableWorkflow = useDisableWorkflow()
  const enableWorkflow = useEnableWorkflow()
  const deprecateWorkflow = useDeprecateWorkflow()
  const startWorkflowExecution = useStartWorkflowExecution()

  const [issues, setIssues] = useState<WorkflowValidationIssue[]>([])
  const [hasValidated, setHasValidated] = useState(false)
  const [publishError, setPublishError] = useState<string | null>(null)
  const [menuAnchorEl, setMenuAnchorEl] = useState<HTMLElement | null>(null)
  const [historyOpen, setHistoryOpen] = useState(false)
  const [executionHistoryOpen, setExecutionHistoryOpen] = useState(false)
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false)
  const [runDialogOpen, setRunDialogOpen] = useState(false)
  const [runInputsJson, setRunInputsJson] = useState('{}')
  const [runError, setRunError] = useState<string | null>(null)
  const [eventTriggerDialogOpen, setEventTriggerDialogOpen] = useState(false)
  const [eventTriggerDraft, setEventTriggerDraft] = useState<string | null>(null)

  const loadedWorkflowId = useRef<string | null>(null)

  useEffect(() => {
    if (!workflow || loadedWorkflowId.current === workflow.id) return
    loadedWorkflowId.current = workflow.id
    loadDefinition(parseDraftDefinition(workflow.draftDefinitionJson))
    setHasValidated(false)
    setIssues([])
  }, [workflow, loadDefinition])

  useEffect(() => () => reset(), [reset])

  useEffect(() => {
    const handler = (event: BeforeUnloadEvent) => {
      if (!isDirty) return
      event.preventDefault()
    }
    window.addEventListener('beforeunload', handler)
    return () => window.removeEventListener('beforeunload', handler)
  }, [isDirty])

  const saveDraft = useCallback(() => {
    if (!workflow) return
    updateWorkflow.mutate(
      { id: workflow.id, input: { name: workflow.name, description: workflow.description, draftDefinitionJson: JSON.stringify(toDraftDefinition()) } },
      { onSuccess: () => markSaved() },
    )
  }, [workflow, updateWorkflow, toDraftDefinition, markSaved])

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      const isMac = navigator.platform.toLowerCase().includes('mac')
      const mod = isMac ? event.metaKey : event.ctrlKey
      if (mod && event.key.toLowerCase() === 's') {
        event.preventDefault()
        saveDraft()
      }
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [saveDraft])

  const runValidate = useCallback(async () => {
    if (!workflow) return []
    const result = await validateWorkflow.mutateAsync(workflow.id)
    setIssues(result)
    setHasValidated(true)
    return result
  }, [workflow, validateWorkflow])

  const handlePublish = useCallback(async () => {
    if (!workflow) return
    setPublishError(null)

    // Save first — publishing materializes whatever the server currently has as the draft, so an
    // unsaved canvas edit would otherwise be silently left out of the published version.
    await updateWorkflow.mutateAsync({
      id: workflow.id,
      input: { name: workflow.name, description: workflow.description, draftDefinitionJson: JSON.stringify(toDraftDefinition()) },
    })
    markSaved()

    const currentIssues = await runValidate()
    if (currentIssues.length > 0) return

    try {
      await publishWorkflowVersion.mutateAsync({ id: workflow.id, changeDescription: null })
    } catch (err) {
      setPublishError(err instanceof Error ? err.message : 'Could not publish this workflow. Please try again.')
    }
  }, [workflow, updateWorkflow, toDraftDefinition, markSaved, runValidate, publishWorkflowVersion])

  const handleConfirmRun = useCallback(() => {
    if (!workflow) return
    setRunError(null)
    startWorkflowExecution.mutate(
      { workflowId: workflow.id, workflowVersionNumber: null, inputsJson: runInputsJson },
      {
        onSuccess: (summary) => {
          setRunDialogOpen(false)
          navigate(`/workflows/${workflow.id}/executions/${summary.id}`)
        },
        onError: (err) => setRunError(err instanceof Error ? err.message : 'Could not start this execution. Please check your inputs and try again.'),
      },
    )
  }, [workflow, runInputsJson, startWorkflowExecution, navigate])

  const handleSaveEventTrigger = useCallback(() => {
    if (!workflow || eventTriggerDraft === null) return
    updateWorkflow.mutate(
      { id: workflow.id, input: { name: workflow.name, description: workflow.description, draftDefinitionJson: workflow.draftDefinitionJson, eventTriggerConfigurationJson: eventTriggerDraft } },
      { onSuccess: () => setEventTriggerDialogOpen(false) },
    )
  }, [workflow, eventTriggerDraft, updateWorkflow])

  if (isLoading || !workflow) {
    return (
      <AppShell title="Loading workflow…">
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 6 }}>
          <CircularProgress />
        </Box>
      </AppShell>
    )
  }

  return (
    <AppShell
      title={workflow.name}
      subtitle={publishError ?? undefined}
      actions={
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          {isDirty && <Chip label="Unsaved changes" size="small" color="warning" variant="outlined" />}
          <Button variant="outlined" startIcon={<SaveIcon />} onClick={saveDraft} disabled={updateWorkflow.isPending}>
            Save Draft
          </Button>
          <Button
            variant="contained"
            startIcon={<PublishIcon />}
            onClick={handlePublish}
            disabled={publishWorkflowVersion.isPending || validateWorkflow.isPending || (hasValidated && issues.length > 0)}
          >
            Publish
          </Button>
          <Button
            variant="outlined"
            startIcon={<PlayArrowIcon />}
            onClick={() => setRunDialogOpen(true)}
            disabled={workflow.publishedVersionNumber === null}
          >
            Run
          </Button>
          <Button variant="text" onClick={() => navigate('/workflows')}>
            Back to Library
          </Button>
          <IconButton aria-label="More workflow actions" onClick={(e) => setMenuAnchorEl(e.currentTarget)}>
            <MoreVertIcon />
          </IconButton>
          <Menu anchorEl={menuAnchorEl} open={Boolean(menuAnchorEl)} onClose={() => setMenuAnchorEl(null)}>
            <MenuItem
              onClick={() => {
                setMenuAnchorEl(null)
                setHistoryOpen(true)
              }}
            >
              <ListItemIcon>
                <HistoryIcon fontSize="small" />
              </ListItemIcon>
              <ListItemText>Version History</ListItemText>
            </MenuItem>
            <MenuItem
              onClick={() => {
                setMenuAnchorEl(null)
                setExecutionHistoryOpen(true)
              }}
            >
              <ListItemIcon>
                <EventNoteIcon fontSize="small" />
              </ListItemIcon>
              <ListItemText>Execution History</ListItemText>
            </MenuItem>
            {workflow.workflowType === 'EventDriven' && (
              <MenuItem
                onClick={() => {
                  setMenuAnchorEl(null)
                  setEventTriggerDraft(workflow.eventTriggerConfigurationJson)
                  setEventTriggerDialogOpen(true)
                }}
              >
                <ListItemIcon>
                  <EventAvailableIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>Event Trigger</ListItemText>
              </MenuItem>
            )}
            {workflow.status === 'Disabled' ? (
              <MenuItem
                disabled={enableWorkflow.isPending}
                onClick={() => {
                  setMenuAnchorEl(null)
                  enableWorkflow.mutate(workflow.id)
                }}
              >
                <ListItemIcon>
                  <EventAvailableIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>Enable</ListItemText>
              </MenuItem>
            ) : (
              workflow.status === 'Published' && (
                <MenuItem
                  disabled={disableWorkflow.isPending}
                  onClick={() => {
                    setMenuAnchorEl(null)
                    disableWorkflow.mutate(workflow.id)
                  }}
                >
                  <ListItemIcon>
                    <EventBusyIcon fontSize="small" />
                  </ListItemIcon>
                  <ListItemText>Disable</ListItemText>
                </MenuItem>
              )
            )}
            {workflow.status === 'Published' && (
              <MenuItem
                disabled={deprecateWorkflow.isPending}
                onClick={() => {
                  setMenuAnchorEl(null)
                  deprecateWorkflow.mutate(workflow.id)
                }}
              >
                <ListItemIcon>
                  <BlockIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>Deprecate</ListItemText>
              </MenuItem>
            )}
            <MenuItem
              disabled={duplicateWorkflow.isPending}
              onClick={() => {
                setMenuAnchorEl(null)
                duplicateWorkflow.mutate(workflow.id, { onSuccess: (copy) => navigate(`/workflows/${copy.id}`) })
              }}
            >
              <ListItemIcon>
                <ContentCopyIcon fontSize="small" />
              </ListItemIcon>
              <ListItemText>Duplicate</ListItemText>
            </MenuItem>
            {workflow.status === 'Archived' ? (
              <MenuItem
                disabled={restoreWorkflow.isPending}
                onClick={() => {
                  setMenuAnchorEl(null)
                  restoreWorkflow.mutate(workflow.id)
                }}
              >
                <ListItemIcon>
                  <UnarchiveIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>Restore</ListItemText>
              </MenuItem>
            ) : (
              <MenuItem
                disabled={archiveWorkflow.isPending}
                onClick={() => {
                  setMenuAnchorEl(null)
                  archiveWorkflow.mutate(workflow.id)
                }}
              >
                <ListItemIcon>
                  <ArchiveIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>Archive</ListItemText>
              </MenuItem>
            )}
            <MenuItem
              onClick={() => {
                setMenuAnchorEl(null)
                setDeleteConfirmOpen(true)
              }}
            >
              <ListItemIcon>
                <DeleteOutlineIcon fontSize="small" color="error" />
              </ListItemIcon>
              <ListItemText sx={{ color: 'error.main' }}>Delete</ListItemText>
            </MenuItem>
          </Menu>
        </Stack>
      }
    >
      <Box sx={{ display: 'flex', height: '100%', minHeight: 0, gap: 2 }}>
        <Box sx={{ width: 280, flexShrink: 0, border: 1, borderColor: 'divider', borderRadius: 1, overflow: 'hidden' }}>
          <NodePalette />
        </Box>

        <Box sx={{ flex: 1, minWidth: 0, border: 1, borderColor: 'divider', borderRadius: 1, overflow: 'hidden' }}>
          <WorkflowCanvas />
        </Box>

        <Box sx={{ width: 360, flexShrink: 0, display: 'flex', flexDirection: 'column', border: 1, borderColor: 'divider', borderRadius: 1, overflow: 'hidden' }}>
          <Box sx={{ flex: 1, minHeight: 0 }}>
            <NodeConfigPanel key={workflow.id} validationIssues={issues} />
          </Box>
          <ValidationPanel issues={issues} hasValidated={hasValidated} isValidating={validateWorkflow.isPending} onValidate={() => void runValidate()} />
        </Box>
      </Box>
      {workflow.status !== 'Draft' && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
          This workflow is {workflow.status.toLowerCase()} — editing its draft here does not change the currently published version until you publish again.
        </Typography>
      )}

      <Dialog open={historyOpen} onClose={() => setHistoryOpen(false)} maxWidth="xs" fullWidth>
        <DialogTitle>Version History</DialogTitle>
        <DialogContent>
          <VersionHistory workflowId={workflow.id} />
        </DialogContent>
      </Dialog>

      <Dialog open={executionHistoryOpen} onClose={() => setExecutionHistoryOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Execution History</DialogTitle>
        <DialogContent>
          <ExecutionHistoryList workflowId={workflow.id} />
        </DialogContent>
      </Dialog>

      <Dialog open={eventTriggerDialogOpen} onClose={() => setEventTriggerDialogOpen(false)} maxWidth="sm" fullWidth aria-labelledby="event-trigger-dialog-title">
        <DialogTitle id="event-trigger-dialog-title">Event Trigger</DialogTitle>
        <DialogContent>
          <EventTriggerConfigPanel
            eventTriggerConfigurationJson={eventTriggerDraft}
            onChange={setEventTriggerDraft}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setEventTriggerDialogOpen(false)} disabled={updateWorkflow.isPending}>
            Cancel
          </Button>
          <Button variant="contained" onClick={handleSaveEventTrigger} disabled={updateWorkflow.isPending}>
            Save
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={runDialogOpen} onClose={() => setRunDialogOpen(false)} maxWidth="sm" fullWidth aria-labelledby="run-workflow-dialog-title">
        <DialogTitle id="run-workflow-dialog-title">Run this workflow</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            Runs the currently published version (v{workflow.publishedVersionNumber}). Provide the inputs as JSON matching this workflow's input schema.
          </DialogContentText>
          <TextField
            label="Inputs (JSON)"
            fullWidth
            multiline
            minRows={4}
            value={runInputsJson}
            onChange={(e) => setRunInputsJson(e.target.value)}
            disabled={startWorkflowExecution.isPending}
          />
          {runError && (
            <Alert severity="error" sx={{ mt: 2 }}>
              {runError}
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRunDialogOpen(false)} disabled={startWorkflowExecution.isPending}>
            Cancel
          </Button>
          <Button variant="contained" onClick={handleConfirmRun} disabled={startWorkflowExecution.isPending}>
            Run
          </Button>
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={deleteConfirmOpen}
        title="Delete this workflow?"
        description="This removes the workflow from your library. Its published versions and execution history are kept for audit purposes."
        confirmLabel="Delete"
        onConfirm={() => {
          setDeleteConfirmOpen(false)
          deleteWorkflow.mutate(workflow.id, { onSuccess: () => navigate('/workflows') })
        }}
        onCancel={() => setDeleteConfirmOpen(false)}
      />
    </AppShell>
  )
}
