import { Alert } from '@mui/material'
import type { ApprovalLinkOutcome } from '../utils/approvalLink'

/** The inline message for an approval deep link that has nothing left to act on (specs/067 US5). */
export function ApprovalLinkNotice({ outcome }: { outcome: ApprovalLinkOutcome }) {
  if (outcome === 'pending') {
    return null
  }

  return outcome === 'decided' ? (
    <Alert severity="info" sx={{ mb: 2 }}>
      This approval has already been decided.
    </Alert>
  ) : (
    <Alert severity="warning" sx={{ mb: 2 }}>
      This approval could not be found.
    </Alert>
  )
}
