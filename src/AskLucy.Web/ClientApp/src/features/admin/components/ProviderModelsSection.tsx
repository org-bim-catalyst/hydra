import { useState } from 'react'
import {
  Box,
  Button,
  Chip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import SyncIcon from '@mui/icons-material/Sync'
import { useQuery } from '@tanstack/react-query'
import { useFormat, useT } from '../../../i18n/useT'
import type { Translate } from '../../../i18n/useT'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import { AiModelStatusMenu } from './AiModelStatusMenu'
import { ModelSyncDialog } from './ModelSyncDialog'

const MODEL_STATUS_COLOR: Record<AdminAiModel['status'], 'success' | 'warning' | 'default'> = {
  Available: 'success',
  Deprecated: 'warning',
  Unavailable: 'default',
}

type T = Translate<'admin.aiProviders'>

const CAPABILITY_KEYS = [
  'streaming',
  'vision',
  'functionCalling',
  'jsonMode',
  'reasoning',
  'embeddings',
  'imageInput',
  'imageOutput',
  'audio',
] as const

const isCapabilityKey = (key: string): key is (typeof CAPABILITY_KEYS)[number] =>
  (CAPABILITY_KEYS as readonly string[]).includes(key)

function formatPricing(pricing: AdminAiModel['pricing'], t: T) {
  if (!pricing) return t('shared.unknown')
  return t('models.pricing', {
    input: `$${pricing.inputPerMillionTokensUsd}`,
    output: `$${pricing.outputPerMillionTokensUsd}`,
  })
}

/**
 * specs/043 FR-030 — absent figures are shown as absent, never as 0. A fabricated 0 is what
 * made these rows unaddable in the first place, and showing one here would misreport a real
 * limit of zero tokens.
 */
function formatTokenLimits(model: AdminAiModel, t: T, formatNumber: (value: number) => string) {
  // specs/043 FR-029a — deliberately *not* the word "Unknown": that is already spoken for by the provider
  // health status and by absent pricing in this very table.
  const notPublished = t('models.notPublished')
  if (model.contextWindowTokens === null && model.maxOutputTokens === null) {
    return notPublished
  }

  const context =
    model.contextWindowTokens === null ? notPublished : formatNumber(model.contextWindowTokens)
  const maxOutput = model.maxOutputTokens === null ? notPublished : formatNumber(model.maxOutputTokens)
  return t('models.tokenLimits', { context, maxOutput })
}

interface ProviderModelsSectionProps {
  provider: AdminAiProvider
}

/** specs/008-ai-model-catalog-management US1-US3 — the expanded content for one provider row. */
export function ProviderModelsSection({ provider }: ProviderModelsSectionProps) {
  const t = useT('admin.aiProviders')
  const format = useFormat()
  const [syncDialogOpen, setSyncDialogOpen] = useState(false)
  const { data: models, isLoading } = useQuery({
    queryKey: ['admin', 'ai-providers', provider.id, 'models'],
    queryFn: () => adminAiProvidersApi.getModels(provider.id),
  })

  return (
    <Box sx={{ p: 2, bgcolor: 'action.hover' }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="subtitle1">{t('models.title')}</Typography>
        <Button
          size="small"
          variant="outlined"
          startIcon={<SyncIcon fontSize="small" />}
          onClick={() => setSyncDialogOpen(true)}
          sx={{
            color: 'text.primary',
            borderColor: 'divider',
            '&:hover': { borderColor: 'text.secondary', bgcolor: 'action.hover' },
          }}
        >
          {t('models.syncFromProvider')}
        </Button>
      </Box>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>{t('models.columns.model')}</TableCell>
            <TableCell>{t('models.columns.capabilities')}</TableCell>
            <TableCell>{t('models.columns.tokenLimits')}</TableCell>
            <TableCell>{t('models.columns.pricing')}</TableCell>
            <TableCell>{t('models.columns.status')}</TableCell>
            <TableCell sx={{ textAlign: 'end' }}>{t('shared.actions')}</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {isLoading && <TableLoadingRow colSpan={6} />}
          {!isLoading && (models ?? []).length === 0 && (
            <TableEmptyRow colSpan={6} message={t('models.emptyTable')} />
          )}
          {models?.map((model) => (
            <TableRow key={model.id}>
              <TableCell>
                <Typography variant="body2">
                  <bdi>{model.displayName}</bdi>
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  <bdi dir="ltr">{model.modelKey}</bdi>
                </Typography>
              </TableCell>
              <TableCell>
                {Object.entries(model.capabilities)
                  .filter(([, supported]) => supported)
                  .map(([capability]) => (
                    <Chip
                      key={capability}
                      size="small"
                      label={isCapabilityKey(capability) ? t(`models.capability.${capability}`) : capability}
                      sx={{ marginInlineEnd: '4px', mb: 0.5 }}
                    />
                  ))}
              </TableCell>
              <TableCell>{formatTokenLimits(model, t, format.number)}</TableCell>
              <TableCell>{formatPricing(model.pricing, t)}</TableCell>
              <TableCell>
                <Chip
                  size="small"
                  label={t(`models.status.${model.status}`)}
                  color={MODEL_STATUS_COLOR[model.status]}
                  variant="outlined"
                />
              </TableCell>
              <TableCell sx={{ textAlign: 'end' }}>
                <AiModelStatusMenu model={model} providerId={provider.id} />
              </TableCell>
            </TableRow>
          ))}
          {models?.length === 0 && (
            <TableRow>
              <TableCell colSpan={6}>
                <Typography variant="body2" color="text.secondary">
                  {t('models.emptyCatalog')}
                </Typography>
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
      <ModelSyncDialog
        providerId={provider.id}
        providerDisplayName={provider.displayName}
        open={syncDialogOpen}
        onClose={() => setSyncDialogOpen(false)}
      />
    </Box>
  )
}
