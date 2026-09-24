import { useState } from 'react'
import {
  Alert,
  Box,
  Paper,
  Snackbar,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
} from '@mui/material'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import { ApiError } from '../../../api/httpClient'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AdminShell } from '../components/AdminShell'
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

  const { data: providers, isLoading: providersLoading } = useQuery({
    queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY,
    queryFn: adminAiProvidersApi.getProviders,
  })

  /**
   * Only language providers that are enabled and hold a credential. A default model on a provider that
   * cannot be used is a setting with nowhere to apply — and the Capabilities page will not offer
   * it either, so listing it here only invites configuring something inert.
   */
  const assignableProviders = (providers ?? []).filter((p) => p.isEnabled && p.hasCredential && adminAiProvidersApi.isLanguageProvider(p))

  /**
   * Every row's model list, fetched here rather than left to each row to discover on its own.
   * The rows still read theirs through the same query key, so this adds no request — what it adds
   * is one place that knows when the table is *finished*. A skeleton exists to cover the wait
   * until content can appear in its final shape; holding it only until the provider list arrived
   * meant rows appeared with empty dropdowns and filled in one by one afterwards.
   */
  const modelQueries = useQueries({
    queries: assignableProviders.map((provider) => ({
      queryKey: ['admin', 'ai-providers', provider.id, 'models'],
      queryFn: () => adminAiProvidersApi.getModels(provider.id),
    })),
  })

  // A failed fetch ends the wait like a successful one does; the row that needed it says so
  // itself, in the caption under its model dropdown.
  const isLoading = providersLoading || modelQueries.some((query) => query.isLoading)

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

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && assignableProviders.length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <AdminShell
      title="Default models"
      subtitle="The model each provider contributes — a capability assigned to a provider runs on the model chosen here"

    >
      <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TableContainer ref={tableRef} component={Paper} variant="outlined" sx={{ flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight, mb: 2 }}>
          <Table size="small" sx={{ height: showsStatusRow ? '100%' : undefined }}>
            <TableHead>
              <TableRow>
                <TableCell>Provider</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Default model</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {isLoading && <TableLoadingRow colSpan={3} />}
              {!isLoading && assignableProviders.length === 0 && (
                <TableEmptyRow colSpan={3} message="No assignable providers found." />
              )}
              {/*
                Gated on the same flag as the skeleton, not just on having data: the provider list
                arrives before the model lists do, and without this the rows painted under the
                skeleton that was still covering their wait.
              */}
              {!isLoading &&
                assignableProviders.map((provider) => (
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

        <Alert severity="info">
          Only providers that are enabled with a credential appear here, and only models marked
          Available on the Providers page can be a default. A provider with no default model cannot
          be assigned to a capability.
        </Alert>
      </Box>

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </AdminShell>
  )
}
