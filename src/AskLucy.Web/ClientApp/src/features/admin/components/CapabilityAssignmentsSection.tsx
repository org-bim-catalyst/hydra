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
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
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

  const { data: assignments } = useQuery({
    queryKey: CAPABILITY_QUERY_KEY,
    queryFn: adminAiProvidersApi.getCapabilityAssignments,
  })

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

  const providerName = (id: string | null) => providers.find((p) => p.id === id)?.displayName ?? null

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
  const hasNoProviders = selectable.length === 0

  return (
    <Box sx={{ mb: 4 }}>
      <Typography variant="h6" sx={{ mb: 1 }}>
        Capabilities
      </Typography>
      <Alert severity="info" sx={{ mb: 2 }}>
        Each capability runs on the provider assigned here, using that provider&apos;s default
        model — except image generation, which runs on the image model you pick for it.
      </Alert>

      {selectable.length === 0 && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          No provider can be assigned yet. Enable a provider with its credential on the Providers
          page, then give it a default model on the Default models page.
        </Alert>
      )}

      <TableContainer component={Paper} variant="outlined">
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Capability</TableCell>
              <TableCell>Assigned provider</TableCell>
              <TableCell>Actually running on</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {assignments?.map((assignment) => {
              // Never index straight into the copy table: the server enumerates the
              // capability enum, so a capability added there before this table knows about it
              // would otherwise throw during render and take the whole page to the error
              // boundary. That is exactly what "Chat" did.
              const copy = CAPABILITY_COPY[assignment.capability] ?? {
                label: assignment.capability,
                consequence: 'No description available for this capability yet.',
              }
              const effective = providerName(assignment.effectiveProviderId)
              if (assignment.capability === 'ImageGeneration') {
                return (
                  <ImageGenerationRow
                    key={assignment.capability}
                    assignment={assignment}
                    label={copy.label}
                    consequence={copy.consequence}
                    providers={providers.filter((p) => p.isEnabled && p.hasCredential)}
                    disabled={assignMutation.isPending}
                    onAssign={(providerId, modelId) =>
                      assignMutation.mutate({ capability: 'ImageGeneration', providerId, modelId })
                    }
                  />
                )
              }
              return (
                <TableRow key={assignment.capability}>
                  <TableCell>
                    <Typography variant="body2">{copy.label}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {copy.consequence}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    {/*
                      A real InputLabel wired through labelId, kept visually hidden: MUI names the
                      combobox from it, so the control has an accessible name for a screen reader
                      instead of an aria-label stranded on the hidden native input.
                    */}
                    <FormControl size="small" sx={{ minWidth: 200 }}>
                      <InputLabel id={`${assignment.capability}-label`} sx={visuallyHidden}>
                        {`Provider for ${copy.label}`}
                      </InputLabel>
                      <Select
                        labelId={`${assignment.capability}-label`}
                        size="small"
                        value={assignment.providerId ?? ''}
                        displayEmpty
                        disabled={hasNoProviders || assignMutation.isPending}
                        onChange={(event) =>
                          assignMutation.mutate({
                            capability: assignment.capability,
                            providerId: event.target.value === '' ? null : event.target.value,
                          })
                        }
                        // The empty value is a placeholder, never a choice — there is no
                        // "platform default" to pick. Until a provider is chosen the control
                        // says so, and with nothing to choose from it says that instead.
                        renderValue={(value) => {
                          const chosen = providerName(value as string)
                          if (chosen) return chosen
                          return (
                            <Typography component="span" variant="body2" color="text.secondary">
                              {hasNoProviders ? 'No AI provider available' : 'Please select AI provider'}
                            </Typography>
                          )
                        }}
                        sx={{ minWidth: 200 }}
                      >
                        {selectable.map((provider) => (
                          <MenuItem key={provider.id} value={provider.id}>
                            {provider.displayName}
                          </MenuItem>
                        ))}
                      </Select>
                    </FormControl>
                  </TableCell>
                  <TableCell>
                    {assignment.providerId && effective ? (
                      <Typography variant="body2">{effective}</Typography>
                    ) : (
                      <Typography variant="body2" color="text.secondary">
                        Not assigned
                      </Typography>
                    )}
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </TableContainer>

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </Box>
  )
}

interface ImageGenerationRowProps {
  assignment: AiCapabilityAssignment
  label: string
  consequence: string
  /** Enabled and credentialled — a default model is irrelevant here, since this row pins its own. */
  providers: AdminAiProvider[]
  disabled: boolean
  onAssign: (providerId: string, modelId: string) => void
}

/**
 * Image generation is the one capability a provider's default model cannot serve (it is a chat
 * model), so this row pins a model as well. Choosing a provider only narrows the model list —
 * nothing is saved until a model is chosen, because the server rejects an image assignment
 * without one rather than store a setting that could never work.
 */
function ImageGenerationRow({ assignment, label, consequence, providers, disabled, onAssign }: ImageGenerationRowProps) {
  const [providerId, setProviderId] = useState<string>(assignment.providerId ?? '')

  const { data: models, isLoading, isError } = useQuery({
    queryKey: ['admin', 'ai-models', providerId],
    queryFn: () => adminAiProvidersApi.getModels(providerId),
    enabled: providerId !== '',
  })

  // Available and able to produce images — the same two rules the server validates.
  const imageModels = (models ?? []).filter((m) => m.status === 'Available' && m.capabilities.imageOutput)
  const assignedModelId = providerId === assignment.providerId ? (assignment.modelId ?? '') : ''
  const runningModel = assignment.effectiveModelId
    ? ((models ?? []).find((m) => m.id === assignment.effectiveModelId)?.displayName ?? null)
    : null
  const runningProvider = providers.find((p) => p.id === assignment.effectiveProviderId)?.displayName ?? null

  const modelPlaceholder = isLoading
    ? 'Loading models…'
    : imageModels.length === 0
      ? 'No image-capable model'
      : 'Please select image model'

  return (
    <TableRow>
      <TableCell>
        <Typography variant="body2">{label}</Typography>
        <Typography variant="caption" color="text.secondary">
          {consequence}
        </Typography>
      </TableCell>
      <TableCell>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
          <FormControl size="small" sx={{ minWidth: 200 }}>
            <InputLabel id="ImageGeneration-label" sx={visuallyHidden}>
              {`Provider for ${label}`}
            </InputLabel>
            <Select
              labelId="ImageGeneration-label"
              size="small"
              value={providerId}
              displayEmpty
              disabled={providers.length === 0 || disabled}
              onChange={(event) => setProviderId(event.target.value)}
              renderValue={(value) =>
                providers.find((p) => p.id === value)?.displayName ?? (
                  <Typography component="span" variant="body2" color="text.secondary">
                    {providers.length === 0 ? 'No AI provider available' : 'Please select AI provider'}
                  </Typography>
                )
              }
            >
              {providers.map((provider) => (
                <MenuItem key={provider.id} value={provider.id}>
                  {provider.displayName}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          {providerId !== '' && (
            <FormControl size="small" sx={{ minWidth: 200 }}>
              <InputLabel id="ImageGeneration-model-label" sx={visuallyHidden}>
                {`Image model for ${label}`}
              </InputLabel>
              <Select
                labelId="ImageGeneration-model-label"
                size="small"
                value={assignedModelId}
                displayEmpty
                disabled={disabled || isLoading || imageModels.length === 0}
                onChange={(event) => onAssign(providerId, event.target.value)}
                renderValue={(value) =>
                  imageModels.find((m) => m.id === value)?.displayName ?? (
                    <Typography component="span" variant="body2" color="text.secondary">
                      {modelPlaceholder}
                    </Typography>
                  )
                }
              >
                {imageModels.map((model) => (
                  <MenuItem key={model.id} value={model.id}>
                    {model.displayName}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
          )}

          {isError && (
            <Typography variant="caption" color="error">
              Couldn&apos;t load this provider&apos;s models.
            </Typography>
          )}
          {!isLoading && !isError && providerId !== '' && imageModels.length === 0 && (
            <Typography variant="caption" color="text.secondary">
              This provider has no Available model marked as able to produce images. Add or enable one on the Models page.
            </Typography>
          )}
        </Box>
      </TableCell>
      <TableCell>
        {assignment.effectiveProviderId && runningProvider ? (
          <Typography variant="body2">{runningModel ? `${runningProvider} · ${runningModel}` : runningProvider}</Typography>
        ) : (
          <Typography variant="body2" color="text.secondary">
            Not configured
          </Typography>
        )}
      </TableCell>
    </TableRow>
  )
}
