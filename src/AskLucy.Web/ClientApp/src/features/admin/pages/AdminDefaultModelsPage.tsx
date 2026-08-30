import { useState } from 'react'
import {
  Alert,
  Button,
  Paper,
  Snackbar,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
} from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AppShell } from '../../../components/AppShell'
import { ProviderDefaultModelRow } from '../components/ProviderDefaultModelRow'

const ADMIN_AI_PROVIDERS_QUERY_KEY = ['admin', 'ai-providers']

/**
 * Step two of three: each provider's default model. Kept on its own page because it is the
 * hinge between the other two — a capability assignment names a provider, and the model it runs
 * on is whatever is chosen here. Burying it inside an expandable row on the Providers page made
 * a platform-wide decision look like a per-provider detail.
 */
export function AdminDefaultModelsPage() {
  const queryClient = useQueryClient()
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(null)

  const { data: providers } = useQuery({
    queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY,
    queryFn: adminAiProvidersApi.getProviders,
  })

  const setDefaultMutation = useMutation({
    mutationFn: ({ providerId, modelId }: { providerId: string; modelId: string | null }) =>
      modelId === null
        ? adminAiProvidersApi.updateProvider(providerId, { clearDefaultModel: true })
        : adminAiProvidersApi.updateProvider(providerId, { defaultModelId: modelId }),
    onSuccess: (_, { modelId }) => {
      void queryClient.invalidateQueries({ queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY })
      setFeedback({
        severity: 'success',
        message: modelId === null ? 'Default model cleared.' : 'Default model saved.',
      })
    },
    // constitution VIII: a failed save must reach the user, not just the console.
    onError: (err: unknown) => {
      setFeedback({
        severity: 'error',
        message: err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.',
      })
    },
  })

  return (
    <AppShell
      title="Default models"
      subtitle="The model each provider contributes — a capability assigned to a provider runs on the model chosen here"
      actions={
        <>
          <Button component={RouterLink} to="/admin/ai-providers" variant="outlined" size="small" sx={{ mr: 1 }}>
            Manage providers
          </Button>
          <Button component={RouterLink} to="/admin/ai-capabilities" variant="outlined" size="small">
            Manage capabilities
          </Button>
        </>
      }
    >
      <Alert severity="info" sx={{ mb: 2 }}>
        Only models marked Available on the Providers page can be a default. A provider with no
        default model cannot be assigned to a capability.
      </Alert>

      <TableContainer component={Paper} variant="outlined">
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Provider</TableCell>
              <TableCell>Status</TableCell>
              <TableCell>Default model</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {providers?.map((provider) => (
              <ProviderDefaultModelRow
                key={provider.id}
                provider={provider}
                disabled={setDefaultMutation.isPending}
                onChange={(modelId) => setDefaultMutation.mutate({ providerId: provider.id, modelId })}
              />
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </AppShell>
  )
}
