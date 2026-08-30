import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  Radio,
  Snackbar,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import SyncIcon from '@mui/icons-material/Sync'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import { AiModelStatusMenu } from './AiModelStatusMenu'
import { ModelSyncDialog } from './ModelSyncDialog'

const ADMIN_AI_PROVIDERS_QUERY_KEY = ['admin', 'ai-providers']

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
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(null)
  const queryClient = useQueryClient()
  const { data: models } = useQuery({
    queryKey: ['admin', 'ai-providers', provider.id, 'models'],
    queryFn: () => adminAiProvidersApi.getModels(provider.id),
  })

  /**
   * The control that was missing entirely: the PATCH endpoint has always accepted
   * `defaultModelId`, but nothing in the UI ever sent it, so every provider sat at null and
   * `DefaultProviderResolver` fell through to its last resort — first enabled provider in
   * display-name order. That is how location intent classification ended up on Anthropic while
   * the user's chat ran on OpenAI.
   */
  const setDefaultMutation = useMutation({
    mutationFn: (modelId: string | null) =>
      modelId === null
        ? adminAiProvidersApi.updateProvider(provider.id, { clearDefaultModel: true })
        : adminAiProvidersApi.updateProvider(provider.id, { defaultModelId: modelId }),
    onSuccess: (_, modelId) => {
      void queryClient.invalidateQueries({ queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY })
      setFeedback({
        severity: 'success',
        message: modelId === null
          ? `${provider.displayName} no longer has a default model.`
          : `Default model set for ${provider.displayName}.`,
      })
    },
    // constitution VIII: a failed mutation must reach the user, not just the console.
    onError: (err: unknown) => {
      setFeedback({
        severity: 'error',
        message: err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.',
      })
    },
  })

  return (
    <Box sx={{ p: 2, bgcolor: 'action.hover' }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="subtitle1">Models</Typography>
        <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
          {provider.defaultModelId && (
            <Button
              size="small"
              color="inherit"
              disabled={setDefaultMutation.isPending}
              onClick={() => setDefaultMutation.mutate(null)}
            >
              Clear default
            </Button>
          )}
          <Button
            size="small"
            startIcon={<SyncIcon fontSize="small" />}
            onClick={() => setSyncDialogOpen(true)}
          >
            Sync from provider
          </Button>
        </Box>
      </Box>
      <Alert severity={provider.isEffectivePlatformDefault ? 'success' : 'info'} sx={{ mb: 1 }}>
        {provider.isEffectivePlatformDefault
          ? `${provider.displayName} is currently the platform default — it serves location intent classification, memory extraction and every other request made without a user's own model preference.`
          : 'The platform default is the first enabled provider, in alphabetical order, that has a default model set. Setting one here does not guarantee this provider wins — clear the default on any provider listed above it that you do not want serving background requests.'}
      </Alert>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell padding="checkbox">Default</TableCell>
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
              <TableCell padding="checkbox">
                {/*
                  Only an Available model may be the default: DefaultProviderResolver requires
                  IsSelectable and would skip a Deprecated/Unavailable one, silently handing the
                  platform default to the next provider alphabetically.
                */}
                <Tooltip
                  title={
                    model.status === 'Available'
                      ? 'Use this model for requests with no user preference'
                      : `Only an Available model can be the default (this one is ${model.status})`
                  }
                >
                  <span>
                    <Radio
                      size="small"
                      checked={provider.defaultModelId === model.id}
                      disabled={model.status !== 'Available' || setDefaultMutation.isPending}
                      onChange={() => setDefaultMutation.mutate(model.id)}
                      slotProps={{ input: { 'aria-label': `Set ${model.displayName} as the default model for ${provider.displayName}` } }}
                    />
                  </span>
                </Tooltip>
              </TableCell>
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
              <TableCell colSpan={7}>
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
      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </Box>
  )
}
