import { useState } from 'react'
import { Alert, Button, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useT } from '../../../i18n/useT'
import { listModelDeprecations } from '../api/modelDeprecationsApi'
import { DeprecationRecordDialog } from './DeprecationRecordDialog'

/**
 * specs/069 FR-011a — surfaces replacement decisions that are still awaiting validation, so a
 * deprecation never sits unnoticed behind a collapsed provider row.
 */
export function DeprecationAttentionBanner() {
  const t = useT('admin.aiProviders')
  const [openId, setOpenId] = useState<string | null>(null)

  const pending = useQuery({
    queryKey: ['admin', 'model-deprecations', 'awaiting-validation'],
    queryFn: () => listModelDeprecations({ awaitingValidation: true }),
  })

  const items = pending.data?.items ?? []
  if (pending.isLoading || items.length === 0) {
    return null
  }

  const active = items.find((item) => item.id === openId) ?? items[0]

  return (
    <>
      <Alert severity="info" sx={{ mb: 2 }}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography variant="body2">{t('sync.openCount', { count: items.length })}</Typography>
          <Button size="small" onClick={() => setOpenId(active.id)}>
            {t('record.title')}
          </Button>
        </Stack>
      </Alert>
      {openId && active && (
        <DeprecationRecordDialog
          deprecationId={active.id}
          providerId={active.providerId}
          open
          onClose={() => setOpenId(null)}
        />
      )}
    </>
  )
}
