import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import type { AgentApproval } from '../api/agentExecutionsApi'
import { useApproveAgentAction, useRejectAgentAction } from '../hooks/useAgentExecution'

interface ApprovalDialogProps {
  executionId: string
  approval: AgentApproval
  onClosed?: () => void
}

/**
 * spec.md FR-025/FR-027 — shown whenever an execution pauses `WaitingForApproval`. Displays the
 * intended action and its parameters before either decision is made (FR-027); the decision itself
 * is permanently recorded server-side regardless of what happens to this dialog afterward
 * (FR-028).
 */
export function ApprovalDialog({ executionId, approval, onClosed }: ApprovalDialogProps) {
  const [reason, setReason] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const approveAction = useApproveAgentAction(executionId)
  const rejectAction = useRejectAgentAction(executionId)

  const busy = approveAction.isPending || rejectAction.isPending

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

  return (
    <Dialog open maxWidth="sm" fullWidth aria-labelledby="agent-approval-dialog-title">
      <DialogTitle id="agent-approval-dialog-title">This agent wants to take an action</DialogTitle>
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
          {approval.intendedParametersJson}
        </Typography>
        <TextField
          label="Reason (optional, shown if you reject)"
          fullWidth
          multiline
          minRows={2}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
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
        <Button onClick={handleApprove} disabled={busy} variant="contained">
          Approve
        </Button>
      </DialogActions>
    </Dialog>
  )
}
