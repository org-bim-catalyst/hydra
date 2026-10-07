import {
  Chip,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  TableCell,
  TableRow,
  Typography,
} from '@mui/material'
import { visuallyHidden } from '@mui/utils'
import { useQuery } from '@tanstack/react-query'
import { useT } from '../../../i18n/useT'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'

interface ProviderDefaultModelRowProps {
  provider: AdminAiProvider
  disabled: boolean
  onChange: (modelId: string | null) => void
}

/**
 * One provider's default model. Its own component because the model list is a per-provider
 * query, and a row must keep reading its own even after the page has drawn.
 *
 * Only Available models are offered: DefaultProviderResolver requires IsSelectable, so a
 * Deprecated or Unavailable default would be skipped at runtime and the capability assigned to
 * this provider would quietly fall back somewhere else.
 */
export function ProviderDefaultModelRow({
  provider,
  disabled,
  onChange,
}: ProviderDefaultModelRowProps) {
  const t = useT('admin.defaultModels')
  // The page issues this exact query for every row it is about to draw, so that it can hold its
  // skeleton until all of them have settled. Sharing the key means this is that same request, not
  // a second one — and the row keeps working when its provider changes after first paint.
  const { data: models, isError: modelsFailed } = useQuery({
    queryKey: ['admin', 'ai-providers', provider.id, 'models'],
    queryFn: () => adminAiProvidersApi.getModels(provider.id),
  })

  const available = (models ?? []).filter((m) => m.status === 'Available')
  const labelId = `${provider.id}-default-model-label`

  return (
    <TableRow>
      <TableCell>
        <Typography variant="body2">{provider.displayName}</Typography>
        <Typography variant="caption" color="text.secondary">
          {provider.providerKey}
        </Typography>
      </TableCell>
      <TableCell>
        {provider.isEnabled ? (
          <Chip size="small" label={t('row.enabled')} color="success" variant="outlined" />
        ) : (
          <Chip size="small" label={t('row.disabled')} variant="outlined" />
        )}
      </TableCell>
      <TableCell>
        <FormControl size="small" sx={{ minWidth: 260 }}>
          <InputLabel id={labelId} sx={visuallyHidden}>
            {t('row.defaultModelFor', { provider: provider.displayName })}
          </InputLabel>
          <Select
            labelId={labelId}
            size="small"
            displayEmpty
            value={provider.defaultModelId ?? ''}
            disabled={disabled || available.length === 0}
            onChange={(event) => onChange(event.target.value === '' ? null : event.target.value)}
          >
            <MenuItem value="">
              <em>{t('row.noDefault')}</em>
            </MenuItem>
            {available.map((model) => (
              <MenuItem key={model.id} value={model.id}>
                {model.displayName}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        {/*
          constitution VIII: a failed fetch leaves the same empty list as a provider with nothing
          marked Available, and the advice below would send the administrator to a page where
          everything already looks correct. Say which of the two it is.
        */}
        {modelsFailed ? (
          <Typography variant="caption" color="error" sx={{ display: 'block', mt: 0.5 }}>
            {t('row.modelsLoadFailed')}
          </Typography>
        ) : (
          available.length === 0 && (
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
              {t('row.noAvailableModels')}
            </Typography>
          )
        )}
      </TableCell>
    </TableRow>
  )
}
