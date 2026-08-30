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
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import { AiModelStatusMenu } from './AiModelStatusMenu'
import { ModelSyncDialog } from './ModelSyncDialog'

const MODEL_STATUS_COLOR: Record<AdminAiModel['status'], 'success' | 'warning' | 'default'> = {
  Available: 'success',
  Deprecated: 'warning',
  Unavailable: 'default',
}

/**
 * specs/043 FR-029a — deliberately *not* the word "Unknown". That word is already spoken for
 * twice on this page: by the provider health status, and by absent pricing in this very table.
 * Reusing it here would collapse three unrelated conditions into one label.
 */
const NOT_PUBLISHED = 'Not published by the vendor'

function formatPricing(pricing: AdminAiModel['pricing']) {
  if (!pricing) return 'Unknown'
  return `$${pricing.inputPerMillionTokensUsd}/$${pricing.outputPerMillionTokensUsd} per 1M tokens (in/out)`
}

/**
 * specs/043 FR-030 — absent figures are shown as absent, never as 0. A fabricated 0 is what
 * made these rows unaddable in the first place, and showing one here would misreport a real
 * limit of zero tokens.
 */
function formatTokenLimits(model: AdminAiModel) {
  if (model.contextWindowTokens === null && model.maxOutputTokens === null) {
    return NOT_PUBLISHED
  }

  const context = model.contextWindowTokens?.toLocaleString() ?? NOT_PUBLISHED
  const maxOutput = model.maxOutputTokens?.toLocaleString() ?? NOT_PUBLISHED
  return `${context} in / ${maxOutput} out`
}

interface ProviderModelsSectionProps {
  provider: AdminAiProvider
}

/** specs/008-ai-model-catalog-management US1-US3 — the expanded content for one provider row. */
export function ProviderModelsSection({ provider }: ProviderModelsSectionProps) {
  const [syncDialogOpen, setSyncDialogOpen] = useState(false)
  const { data: models } = useQuery({
    queryKey: ['admin', 'ai-providers', provider.id, 'models'],
    queryFn: () => adminAiProvidersApi.getModels(provider.id),
  })

  return (
    <Box sx={{ p: 2, bgcolor: 'action.hover' }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="subtitle1">Models</Typography>
        <Button
          size="small"
          startIcon={<SyncIcon fontSize="small" />}
          onClick={() => setSyncDialogOpen(true)}
        >
          Sync from provider
        </Button>
      </Box>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Model</TableCell>
            <TableCell>Capabilities</TableCell>
            <TableCell>Token limits</TableCell>
            <TableCell>Pricing</TableCell>
            <TableCell>Status</TableCell>
            <TableCell align="right">Actions</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {models?.map((model) => (
            <TableRow key={model.id}>
              <TableCell>
                <Typography variant="body2">{model.displayName}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {model.modelKey}
                </Typography>
              </TableCell>
              <TableCell>
                {Object.entries(model.capabilities)
                  .filter(([, supported]) => supported)
                  .map(([capability]) => (
                    <Chip
                      key={capability}
                      size="small"
                      label={capability}
                      sx={{ mr: 0.5, mb: 0.5 }}
                    />
                  ))}
              </TableCell>
              <TableCell>{formatTokenLimits(model)}</TableCell>
              <TableCell>{formatPricing(model.pricing)}</TableCell>
              <TableCell>
                <Chip
                  size="small"
                  label={model.status}
                  color={MODEL_STATUS_COLOR[model.status]}
                  variant="outlined"
                />
              </TableCell>
              <TableCell align="right">
                <AiModelStatusMenu model={model} providerId={provider.id} />
              </TableCell>
            </TableRow>
          ))}
          {models?.length === 0 && (
            <TableRow>
              <TableCell colSpan={6}>
                <Typography variant="body2" color="text.secondary">
                  No models in the catalog yet — try syncing from the provider.
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
