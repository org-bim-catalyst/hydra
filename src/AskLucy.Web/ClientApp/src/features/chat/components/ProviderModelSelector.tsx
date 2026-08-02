import { MenuItem, TextField } from '@mui/material'
import { useEffect, useState } from 'react'
import { useAiModels, useAiProviders } from '../hooks/useAiCatalog'

interface ProviderModelSelectorProps {
  providerId: string | null
  modelId: string | null
  onSelect: (providerId: string, modelId: string) => void
}

/**
 * specs/005-multi-provider-ai-engine FR-008/FR-009 — lets the user pick any enabled
 * provider/model, mirroring LanguageSelector.tsx's `TextField select` convention.
 * `onSelect` is only ever called with a complete, valid pair — a provider change is held
 * locally (`draftProviderId`) until that provider's own model list loads, so the parent
 * (and the model-selection PATCH it triggers) never sees a provider/model mismatch.
 */
export function ProviderModelSelector({ providerId, modelId, onSelect }: ProviderModelSelectorProps) {
  const { data: providers } = useAiProviders()
  const [draftProviderId, setDraftProviderId] = useState<string | null>(null)
  // Falls back to the first provider (once loaded) whenever neither an explicit draft nor
  // the `providerId` prop is set, computed during render rather than via an effect + setState
  // (react-hooks/set-state-in-effect) — this is purely derived from other reactive values.
  const effectiveProviderId = draftProviderId ?? providerId ?? providers?.[0]?.id ?? null
  const { data: models } = useAiModels(effectiveProviderId)

  // React's sanctioned "adjust state during render" pattern (not an effect): whenever the
  // `providerId` prop changes — which happens once the parent adopts a selection this
  // component committed via `onSelect` below — the local draft override is no longer needed.
  const [syncedProviderId, setSyncedProviderId] = useState(providerId)
  if (providerId !== syncedProviderId) {
    setSyncedProviderId(providerId)
    setDraftProviderId(null)
  }

  useEffect(() => {
    if (effectiveProviderId && models && models.length > 0 && (effectiveProviderId !== providerId || !modelId)) {
      onSelect(effectiveProviderId, models[0].id)
    }
  }, [effectiveProviderId, providerId, modelId, models, onSelect])

  if (!providers || providers.length === 0) {
    return null
  }

  return (
    <>
      <TextField
        select
        size="small"
        label="Provider"
        value={effectiveProviderId ?? ''}
        onChange={(e) => setDraftProviderId(e.target.value)}
        sx={{ minWidth: 140 }}
      >
        {providers.map((provider) => (
          <MenuItem key={provider.id} value={provider.id}>
            {provider.displayName}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        select
        size="small"
        label="Model"
        value={modelId ?? ''}
        disabled={!models || models.length === 0}
        onChange={(e) => effectiveProviderId && onSelect(effectiveProviderId, e.target.value)}
        sx={{ minWidth: 160 }}
      >
        {(models ?? []).map((model) => (
          <MenuItem key={model.id} value={model.id}>
            {model.displayName}
          </MenuItem>
        ))}
      </TextField>
    </>
  )
}
