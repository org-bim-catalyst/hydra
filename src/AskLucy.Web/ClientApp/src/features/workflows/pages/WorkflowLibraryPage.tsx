import AddIcon from '@mui/icons-material/Add'
import {
  Alert,
  Box,
  Button,
  Card,
  CardActionArea,
  CardActions,
  CardContent,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import { EmptyState } from '../../../components/EmptyState'
import type { WorkflowType } from '../api/workflowsApi'
import { StatisticsDashboard } from '../components/StatisticsDashboard'
import {
  useArchiveWorkflow,
  useCreateWorkflow,
  useDeleteWorkflow,
  useDeprecateWorkflow,
  useDisableWorkflow,
  useDuplicateWorkflow,
  useEnableWorkflow,
  useRestoreWorkflow,
} from '../hooks/useWorkflowMutations'
import { useSearchWorkflows } from '../hooks/useWorkflows'

interface CreateWorkflowFormValues {
  name: string
  description: string
  workflowType: WorkflowType
}

/**
 * Workflow Library — a user's own workflows (spec.md User Story 1) plus a jump-off point into the
 * Designer (User Story 2). Creation is a lightweight name/description/type dialog; the rest of a
 * workflow's authoring (steps, connections, variables) happens after navigating into
 * `WorkflowDesignerPage`, mirroring `AgentLibraryPage` → `AgentBuilderPage`'s own flow.
 */
export function WorkflowLibraryPage() {
  const navigate = useNavigate()
  const { data, isLoading } = useSearchWorkflows({ pageSize: 50 })
  const workflows = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data])
  const createWorkflow = useCreateWorkflow()
  const duplicateWorkflow = useDuplicateWorkflow()
  const archiveWorkflow = useArchiveWorkflow()
  const restoreWorkflow = useRestoreWorkflow()
  const deleteWorkflow = useDeleteWorkflow()
  const disableWorkflow = useDisableWorkflow()
  const enableWorkflow = useEnableWorkflow()
  const deprecateWorkflow = useDeprecateWorkflow()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null)

  const { register, handleSubmit, formState, reset } = useForm<CreateWorkflowFormValues>({
    defaultValues: { name: '', description: '', workflowType: 'Manual' },
  })

  const closeDialog = () => {
    setDialogOpen(false)
    setErrorMessage(null)
    reset()
  }

  const submit = handleSubmit((values) => {
    createWorkflow.mutate(
      { name: values.name, description: values.description || null, workflowType: values.workflowType },
      {
        onSuccess: (created) => {
          closeDialog()
          navigate(`/workflows/${created.id}`)
        },
        onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not create the workflow. Please try again.'),
      },
    )
  })

  return (
    <AppShell
      title="Workflows"
      actions={
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialogOpen(true)}>
          New Workflow
        </Button>
      }
    >
      <Box sx={{ p: 3 }}>
        <StatisticsDashboard />

        {!isLoading && workflows.length === 0 && (
          <EmptyState title="No workflows yet" description="Create your first workflow to get started." />
        )}

        <Stack spacing={2}>
          {workflows.map((workflow) => (
            <Card key={workflow.id} variant="outlined">
              <CardActionArea onClick={() => navigate(`/workflows/${workflow.id}`)}>
                <CardContent>
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 0.5 }}>
                    <Typography variant="subtitle1">{workflow.name}</Typography>
                    <Chip label={workflow.status} size="small" />
                    <Chip label={workflow.workflowType} size="small" variant="outlined" />
                  </Stack>
                  {workflow.description && (
                    <Typography variant="body2" color="text.secondary">
                      {workflow.description}
                    </Typography>
                  )}
                </CardContent>
              </CardActionArea>
              <CardActions>
                <Button size="small" onClick={() => duplicateWorkflow.mutate(workflow.id)} disabled={duplicateWorkflow.isPending}>
                  Duplicate
                </Button>
                {workflow.status === 'Archived' ? (
                  <Button size="small" onClick={() => restoreWorkflow.mutate(workflow.id)} disabled={restoreWorkflow.isPending}>
                    Restore
                  </Button>
                ) : (
                  <Button size="small" onClick={() => archiveWorkflow.mutate(workflow.id)} disabled={archiveWorkflow.isPending}>
                    Archive
                  </Button>
                )}
                {workflow.status === 'Disabled' && (
                  <Button size="small" onClick={() => enableWorkflow.mutate(workflow.id)} disabled={enableWorkflow.isPending}>
                    Enable
                  </Button>
                )}
                {workflow.status === 'Published' && (
                  <>
                    <Button size="small" onClick={() => disableWorkflow.mutate(workflow.id)} disabled={disableWorkflow.isPending}>
                      Disable
                    </Button>
                    <Button size="small" onClick={() => deprecateWorkflow.mutate(workflow.id)} disabled={deprecateWorkflow.isPending}>
                      Deprecate
                    </Button>
                  </>
                )}
                <Button size="small" color="error" onClick={() => setPendingDeleteId(workflow.id)}>
                  Delete
                </Button>
              </CardActions>
            </Card>
          ))}
        </Stack>
      </Box>

      <Dialog open={dialogOpen} onClose={closeDialog} maxWidth="sm" fullWidth aria-labelledby="create-workflow-dialog-title">
        <DialogTitle id="create-workflow-dialog-title">New Workflow</DialogTitle>
        <Box component="form" onSubmit={submit}>
          <DialogContent>
            <Stack spacing={2.5}>
              {errorMessage && <Alert severity="error">{errorMessage}</Alert>}
              <TextField
                label="Name"
                fullWidth
                autoFocus
                required
                {...register('name', { required: 'A workflow name is required.' })}
                error={Boolean(formState.errors.name)}
                helperText={formState.errors.name?.message}
              />
              <TextField label="Description" fullWidth multiline rows={3} {...register('description')} />
              <TextField select label="Workflow Type" fullWidth {...register('workflowType')}>
                <MenuItem value="Manual">Manual</MenuItem>
                <MenuItem value="EventDriven">Event-Driven</MenuItem>
                <MenuItem value="AgentAssisted">Agent-Assisted</MenuItem>
              </TextField>
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={closeDialog}>Cancel</Button>
            <Button type="submit" variant="contained" disabled={createWorkflow.isPending}>
              Create Workflow
            </Button>
          </DialogActions>
        </Box>
      </Dialog>

      <ConfirmDialog
        open={pendingDeleteId !== null}
        title="Delete this workflow?"
        description="This removes the workflow from your library. Its published versions and execution history are kept for audit purposes."
        confirmLabel="Delete"
        onConfirm={() => {
          if (pendingDeleteId) deleteWorkflow.mutate(pendingDeleteId)
          setPendingDeleteId(null)
        }}
        onCancel={() => setPendingDeleteId(null)}
      />
    </AppShell>
  )
}
