import { useState } from 'react'
import {
  Alert,
  Box,
  FormControl,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Snackbar,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { visuallyHidden } from '@mui/utils'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import { ApiError } from '../../../api/httpClient'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiProvider, AiCapability, AiCapabilityAssignment } from '../api/adminAiProvidersApi'

const CAPABILITY_QUERY_KEY = ['admin', 'ai-capabilities']

/**
 * Plain-language names and, more usefully, what breaks when the assigned provider stops working.
 * "LocationIntent" tells an administrator nothing; "the viewer never moves" tells them why they
 * are on this screen.
 */
const CAPABILITY_COPY: Record<AiCapability, { label: string; consequence: string }> = {
  Chat: {
    label: 'Chat',
    consequence: 'Answers the user in conversation. Every other capability here is background work.',
  },
  LocationIntent: {
    label: 'Location intent',
    consequence: 'Decides whether a message asks to view a place. Without it the viewer never moves.',
  },
  MemoryExtraction: {
    label: 'Memory extraction',
    consequence: 'Reads finished conversations for facts worth remembering.',
  },
  MemoryConflictDetection: {
    label: 'Memory conflict detection',
    consequence: 'Decides whether a new memory contradicts a stored one.',
  },
  DocumentClassification: {
    label: 'Document language and classification',
    consequence: 'Detects the language and type of an uploaded document.',
  },
  BoundaryVision: {
    label: 'Boundary vision',
    consequence: 'Cross-checks a site boundary against satellite imagery. Currently requires Google Gemini.',
  },
  TurnOrchestration: {
    label: 'Turn orchestration',
    consequence: 'Decides what each chat turn needs — whether to act, and which capability to run.',
  },
  ImageGeneration: {
    label: 'Image generation',
    consequence:
      'Draws images from a prompt — chat images and the site analysis map. Needs an image-capable model; it never falls back to a chat model.',
  },
}

type AssignVariables = { capability: AiCapability; providerId: string | null; modelId?: string }

interface CapabilityAssignmentsSectionProps {
  providers: AdminAiProvider[]
}

/**
 * The third of the three settings that decide which model runs what: models are marked
 * Available, each provider gets a default model, and each capability gets a provider — from
 * which the model follows automatically.
 *
 * Before this existed these capabilities picked their provider by falling through
 * DefaultProviderResolver's last resort, "first enabled provider in display-name order". Nobody
 * chose that, and it silently routed location intent classification to a provider whose credit
 * had run out while the operator's own chat ran fine on another.
 */
export function CapabilityAssignmentsSection({ providers }: CapabilityAssignmentsSectionProps) {
  const queryClient = useQueryClient()
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(null)

  const { data: assignments, isLoading: assignmentsLoading } = useQuery({
    queryKey: CAPABILITY_QUERY_KEY,
    queryFn: adminAiProvidersApi.getCapabilityAssignments,
  })

  /**
   * The model list behind every already-assigned row, fetched here rather than left to each row.
   * The rows read theirs through the same query key, so this adds no request — what it adds is one
   * place that knows when the table is *finished*. A skeleton covers the wait until content can
   * appear in its final shape; holding it only until the assignments arrived meant rows appeared
   * with "Loading models…" in every model dropdown and settled one by one afterwards.
   */
  const assignedProviderIds = [...new Set((assignments ?? []).map((a) => a.providerId).filter((id) => id !== null))]
  const modelQueries = useQueries({
    queries: assignedProviderIds.map((providerId) => ({
      queryKey: ['admin', 'ai-models', providerId],
      queryFn: () => adminAiProvidersApi.getModels(providerId),
    })),
  })

  // A failed fetch ends the wait like a successful one does; the row that needed it says so
  // itself, in the caption under its model dropdown.
  const isLoading = assignmentsLoading || modelQueries.some((query) => query.isLoading)

  const assignMutation = useMutation({
    mutationFn: ({ capability, providerId, modelId }: AssignVariables) =>
      modelId
        ? adminAiProvidersApi.setCapabilityAssignment(capability, providerId, modelId)
        : adminAiProvidersApi.setCapabilityAssignment(capability, providerId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: CAPABILITY_QUERY_KEY })
      setFeedback({ severity: 'success', message: 'Capability assignment saved.' })
    },
    // constitution VIII: a rejected assignment must reach the user. The server explains exactly
    // why — a provider that is not enabled, or one with no usable default model.
    onError: (err: unknown) => {
      setFeedback({
        severity: 'error',
        message: err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.',
      })
    },
  })

  /**
   * A provider is offerable only once all three prerequisites hold: enabled, credentialled, and
   * carrying a default model. The model is what the capability actually runs on, so a provider
   * without one would store an assignment that DefaultProviderResolver immediately falls back
   * from — configured, and silently doing nothing.
   *
   * The missing default model is no longer explained inline here, because it now has a page of
   * its own: Default models, the step between Providers and this one.
   */
  const selectable = providers.filter((p) => p.isEnabled && p.hasCredential && p.defaultModelId)

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && (assignments ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
      <TableContainer ref={tableRef} component={Paper} variant="outlined" sx={{ flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight, mb: 2 }}>
        <Table size="small" sx={{ height: showsStatusRow ? '100%' : undefined }}>
          <TableHead>
            <TableRow>
              <TableCell>Capability</TableCell>
              <TableCell>Assigned provider</TableCell>
              <TableCell>Model</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={3} />}
            {!isLoading && (assignments ?? []).length === 0 && (
              <TableEmptyRow colSpan={3} message="No capabilities found." />
            )}
            {/*
              Gated on the same flag as the skeleton, not just on having data: the assignments
              arrive before the model lists do, and without this the rows painted under the
              skeleton that was still covering their wait.
            */}
            {!isLoading &&
              assignments?.map((assignment) => {
                // Never index straight into the copy table: the server enumerates the
                // capability enum, so a capability added there before this table knows about it
                // would otherwise throw during render and take the whole page to the error
                // boundary. That is exactly what "Chat" did.
                const copy = CAPABILITY_COPY[assignment.capability] ?? {
                  label: assignment.capability,
                  consequence: 'No description available for this capability yet.',
                }
                return (
                  <CapabilityRow
                    key={assignment.capability}
                    assignment={assignment}
                    label={copy.label}
                    consequence={copy.consequence}
                    // Image generation pins its own model, so a provider without a default model is
                    // still usable for it; every other capability falls back to that default and
                    // would otherwise store a setting that silently does nothing.
                    providers={
                      assignment.capability === 'ImageGeneration'
                        ? providers.filter((p) => p.isEnabled && p.hasCredential)
                        : selectable
                    }
                    disabled={assignMutation.isPending}
                    onAssign={(providerId, modelId) =>
                      assignMutation.mutate({
                        capability: assignment.capability,
                        providerId,
                        modelId: modelId ?? undefined,
                      })
                    }
                  />
                )
              })}
          </TableBody>
        </Table>
      </TableContainer>

      <Alert severity="info">
        Each capability runs on the provider assigned here. Leave its model on &ldquo;Provider
        default&rdquo; to follow that provider&apos;s default model, or pick a different one of
        its models for this capability alone. Image generation must pick an image-capable model:
        a provider&apos;s default is a chat model and cannot draw.
      </Alert>

      {selectable.length === 0 && (
        <Alert severity="warning" sx={{ mt: 2 }}>
          No provider can be assigned yet. Enable a provider with its credential on the Providers
          page, then give it a default model on the Default models page.
        </Alert>
      )}

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </Box>
  )
}

interface CapabilityRowProps {
  assignment: AiCapabilityAssignment
  label: string
  consequence: string
  /** The providers offerable for this capability. */
  providers: AdminAiProvider[]
  disabled: boolean
  onAssign: (providerId: string | null, modelId: string | null) => void
}

/**
 * One capability: which provider serves it, and which of that provider's models it runs on.
 *
 * The model is chosen per capability rather than once per provider, because the same provider
 * serves different capabilities best with different models — chat on a fast, cheap chat model,
 * image generation on an image model no chat model could stand in for. Leaving the model unset
 * falls back to the provider's own default, which is what the dropdown shows as its placeholder.
 *
 * Image generation is the one capability that cannot fall back: a provider's default model is a
 * chat model, and the server rejects an image assignment with no image-capable model pinned. So
 * there the provider choice is held locally until a model is picked, and "Provider default" is
 * not offered at all.
 */
function CapabilityRow({ assignment, label, consequence, providers, disabled, onAssign }: CapabilityRowProps) {
  const needsPinnedModel = assignment.capability === 'ImageGeneration'
  const [providerId, setProviderId] = useState<string>(assignment.providerId ?? '')

  const {
    data: models,
    isLoading: modelsLoading,
    isError: modelsFailed,
  } = useQuery({
    queryKey: ['admin', 'ai-models', providerId],
    queryFn: () => adminAiProvidersApi.getModels(providerId),
    enabled: providerId !== '',
  })

  // The same two rules the server validates, plus image output where the capability demands it.
  const selectableModels = (models ?? []).filter(
    (model) => model.status === 'Available' && (!needsPinnedModel || model.capabilities.imageOutput),
  )

  // Only meaningful while the row still shows the saved provider: after switching, the saved pin
  // belongs to the provider that was replaced.
  const pinnedModelId = providerId === assignment.providerId ? (assignment.modelId ?? '') : ''
  const provider = providers.find((p) => p.id === providerId)
  const providerDefaultModel = provider?.defaultModelId
    ? ((models ?? []).find((m) => m.id === provider.defaultModelId)?.displayName ?? null)
    : null

  const providerDefaultLabel = providerDefaultModel ? `Provider default · ${providerDefaultModel}` : 'Provider default'
  const modelPlaceholder = modelsLoading
    ? 'Loading models…'
    : selectableModels.length === 0
      ? needsPinnedModel
        ? 'No image-capable model'
        : 'No model available'
      : needsPinnedModel
        ? 'Please select image model'
        : providerDefaultLabel

  const handleProviderChange = (value: string) => {
    setProviderId(value)
    // Nothing to save yet for image generation — without a model the server rejects it outright.
    if (needsPinnedModel) return
    onAssign(value === '' ? null : value, null)
  }

  return (
    <TableRow>
      <TableCell>
        <Typography variant="body2">{label}</Typography>
        <Typography variant="caption" color="text.secondary">
          {consequence}
        </Typography>
      </TableCell>
      <TableCell>
        {/*
          A real InputLabel wired through labelId, kept visually hidden: MUI names the combobox
          from it, so the control has an accessible name for a screen reader instead of an
          aria-label stranded on the hidden native input.
        */}
        <FormControl size="small" sx={{ minWidth: 200 }}>
          <InputLabel id={`${assignment.capability}-label`} sx={visuallyHidden}>
            {`Provider for ${label}`}
          </InputLabel>
          <Select
            labelId={`${assignment.capability}-label`}
            size="small"
            value={providerId}
            displayEmpty
            disabled={providers.length === 0 || disabled}
            onChange={(event) => handleProviderChange(event.target.value)}
            // The empty value is a placeholder, never a choice — there is no "platform default"
            // to pick. Until a provider is chosen the control says so, and with nothing to choose
            // from it says that instead.
            renderValue={(value) =>
              providers.find((p) => p.id === value)?.displayName ?? (
                <Typography component="span" variant="body2" color="text.secondary">
                  {providers.length === 0 ? 'No AI provider available' : 'Please select AI provider'}
                </Typography>
              )
            }
          >
            {providers.map((option) => (
              <MenuItem key={option.id} value={option.id}>
                {option.displayName}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </TableCell>
      <TableCell>
        {providerId === '' ? (
          <Typography variant="body2" color="text.secondary">
            Assign a provider first
          </Typography>
        ) : (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
            <FormControl size="small" sx={{ minWidth: 220 }}>
              <InputLabel id={`${assignment.capability}-model-label`} sx={visuallyHidden}>
                {`Model for ${label}`}
              </InputLabel>
              <Select
                labelId={`${assignment.capability}-model-label`}
                size="small"
                value={pinnedModelId}
                displayEmpty
                disabled={disabled || modelsLoading || selectableModels.length === 0}
                onChange={(event) => onAssign(providerId, event.target.value === '' ? null : event.target.value)}
                renderValue={(value) =>
                  selectableModels.find((m) => m.id === value)?.displayName ?? (
                    <Typography component="span" variant="body2" color="text.secondary">
                      {modelPlaceholder}
                    </Typography>
                  )
                }
              >
                {!needsPinnedModel && <MenuItem value="">{providerDefaultLabel}</MenuItem>}
                {selectableModels.map((model) => (
                  <MenuItem key={model.id} value={model.id}>
                    {model.displayName}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            {modelsFailed && (
              <Typography variant="caption" color="error">
                Couldn&apos;t load this provider&apos;s models.
              </Typography>
            )}
            {needsPinnedModel && !modelsLoading && !modelsFailed && selectableModels.length === 0 && (
              <Typography variant="caption" color="text.secondary">
                This provider has no Available model marked as able to produce images. Add or enable one on the Models page.
              </Typography>
            )}
          </Box>
        )}
      </TableCell>
    </TableRow>
  )
}
