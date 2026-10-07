import { Alert } from '@mui/material'
import { useT } from '../../../i18n/useT'
import type { ApprovalLinkOutcome } from '../utils/approvalLink'

/** The inline message for an approval deep link that has nothing left to act on (specs/067 US5). */
export function ApprovalLinkNotice({ outcome }: { outcome: ApprovalLinkOutcome }) {
  const t = useT('notifications')
  if (outcome === 'pending') {
    return null
  }

  return outcome === 'decided' ? (
    <Alert severity="info" sx={{ mb: 2 }}>
      {t('approval.decided')}
    </Alert>
  ) : (
    <Alert severity="warning" sx={{ mb: 2 }}>
      {t('approval.notFound')}
    </Alert>
  )
}
