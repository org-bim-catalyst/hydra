import { useQuery } from '@tanstack/react-query'
import * as aiProvidersApi from '../api/aiProvidersApi'

export function useAiProviders() {
  return useQuery({ queryKey: ['ai', 'providers'], queryFn: aiProvidersApi.getEnabledProviders })
}

export function useAiModels(providerId: string | null) {
  return useQuery({
    queryKey: ['ai', 'providers', providerId, 'models'],
    queryFn: () => aiProvidersApi.getModelsForProvider(providerId!),
    enabled: providerId !== null,
  })
}
