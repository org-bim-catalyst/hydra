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
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'

interface ProviderDefaultModelRowProps {
  provider: AdminAiProvider
  disabled: boolean
  onChange: (modelId: string | null) => void
}

/**
 * One provider's default model. Its own component because the model list is a per-provider
 * query — a single component for the whole table would have to fetch every provider's models
 * at once, or fetch inside a loop, neither of which React or TanStack Query allow cleanly.
 *
 * Only Available models are offered: DefaultProviderResolver requires IsSelectable, so a
 * Deprecated or Unavailable default would be skipped at runtime and the capability assigned to
 * this provider would quietly fall back somewhere else.
 */
export function ProviderDefaultModelRow({ provider, disabled, onChange }: ProviderDefaultModelRowProps) {
  const { data: models } = useQuery({
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
          <Chip size="small" label="Enabled" color="success" variant="outlined" />
        ) : (
          <Chip size="small" label="Disabled" variant="outlined" />
        )}
      </TableCell>
      <TableCell>
        <FormControl size="small" sx={{ minWidth: 260 }}>
          <InputLabel id={labelId} sx={visuallyHidden}>
            {`Default model for ${provider.displayName}`}
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
              <em>No default</em>
            </MenuItem>
            {available.map((model) => (
              <MenuItem key={model.id} value={model.id}>
                {model.displayName}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        {available.length === 0 && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
            No Available models — mark one Available on the Providers page first.
          </Typography>
        )}
      </TableCell>
    </TableRow>
  )
}
