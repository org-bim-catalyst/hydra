import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import type { WorkflowApproval } from '../api/workflowExecutionsApi'
import { useApproveWorkflowNode, useRejectWorkflowNode, useRequestWorkflowNodeChanges } from '../hooks/useWorkflowExecution'

interface ApprovalDialogProps {
  executionId: string
  approval: WorkflowApproval
  onClosed?: () => void
}

/**
 * spec.md User Story 5 — shown whenever a workflow execution pauses `WaitingForApproval`. Displays
 * the intended action and its parameters before a decision is made. Unlike the Agent Runtime's
 * two-decision precedent (Approve/Reject), workflows support a third "Request Changes" decision —
 * it always requires comments describing what needs to change, since without them it carries no
 * more information than a plain rejection.
 */
export function ApprovalDialog({ executionId, approval, onClosed }: ApprovalDialogProps) {
  const [reason, setReason] = useState('')
  const [comments, setComments] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const approveAction = useApproveWorkflowNode(executionId)
  const rejectAction = useRejectWorkflowNode(executionId)
  const requestChangesAction = useRequestWorkflowNodeChanges(executionId)

  const busy = approveAction.isPending || rejectAction.isPending || requestChangesAction.isPending

  const handleApprove = () => {
    setErrorMessage(null)
    approveAction.mutate(approval.id, {
      onSuccess: () => onClosed?.(),
      onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not approve this action. Please try again.'),
    })
  }

  const handleReject = () => {
    setErrorMessage(null)
    rejectAction.mutate(
      { approvalId: approval.id, reason: reason || null },
      {
        onSuccess: () => onClosed?.(),
        onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not reject this action. Please try again.'),
      },
    )
  }

  const handleRequestChanges = () => {
    if (!comments.trim()) {
      setErrorMessage('Comments are required when requesting changes.')
      return
    }

    setErrorMessage(null)
    requestChangesAction.mutate(
      { approvalId: approval.id, comments },
      {
        onSuccess: () => onClosed?.(),
        onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not submit your requested changes. Please try again.'),
      },
    )
  }

  return (
    <Dialog open maxWidth="sm" fullWidth aria-labelledby="workflow-approval-dialog-title">
      <DialogTitle id="workflow-approval-dialog-title">This workflow step needs your approval</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>{approval.intendedActionDescription}</DialogContentText>
        <Typography variant="caption" color="text.secondary">
          Parameters
        </Typography>
        <Typography
          component="pre"
          variant="body2"
          data-testid="approval-parameters"
          sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 1, overflowX: 'auto', mt: 0.5, mb: 2 }}
        >
          {approval.parametersJson}
        </Typography>
        <TextField
          label="Reason (optional, shown if you reject)"
          fullWidth
          multiline
          minRows={2}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          disabled={busy}
          sx={{ mb: 2 }}
        />
        <TextField
          label="Comments (required to request changes)"
          fullWidth
          multiline
          minRows={2}
          value={comments}
          onChange={(e) => setComments(e.target.value)}
          disabled={busy}
        />
        {errorMessage && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {errorMessage}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={handleReject} disabled={busy} color="error">
          Reject
        </Button>
        <Button onClick={handleRequestChanges} disabled={busy}>
          Request Changes
        </Button>
        <Button onClick={handleApprove} disabled={busy} variant="contained">
          Approve
        </Button>
      </DialogActions>
    </Dialog>
  )
}
