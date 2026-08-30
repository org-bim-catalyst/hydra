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
import type { AdminAiProvider, AiCapability } from '../api/adminAiProvidersApi'

const CAPABILITY_QUERY_KEY = ['admin', 'ai-capabilities']

/**
 * Plain-language names and, more usefully, what breaks when the assigned provider stops working.
 * "LocationIntent" tells an administrator nothing; "the viewer never moves" tells them why they
 * are on this screen.
 */
const CAPABILITY_COPY: Record<AiCapability, { label: string; consequence: string }> = {
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
}

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
    mutationFn: ({ capability, providerId }: { capability: AiCapability; providerId: string | null }) =>
      adminAiProvidersApi.setCapabilityAssignment(capability, providerId),
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
   * Every enabled provider with a credential is listed, so the menu matches what the Providers
   * page shows as usable. An earlier version also required a default model and silently dropped
   * the rest, which left the list showing OpenAI alone while Anthropic and Google Gemini sat
   * enabled and configured — the admin could see no reason why they were missing.
   *
   * A provider still cannot be *chosen* until it has a default model, because the model is what
   * the capability actually runs on. That is now shown as a disabled row explaining the missing
   * step rather than as an absence explaining nothing.
   */
  const selectable = providers.filter((p) => p.isEnabled && p.hasCredential)

  return (
    <Box sx={{ mb: 4 }}>
      <Typography variant="h6" sx={{ mb: 1 }}>
        Capabilities
      </Typography>
      <Alert severity="info" sx={{ mb: 2 }}>
        Each capability runs on the provider assigned here, using that provider&apos;s default
        model. Leave one unassigned and it falls back to the platform default, which is decided
        alphabetically — the behaviour this screen exists to replace.
      </Alert>

      {selectable.length === 0 && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          No provider can be assigned yet. Enable a provider and configure its credential on the
          Providers page first.
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
              const copy = CAPABILITY_COPY[assignment.capability]
              const effective = providerName(assignment.effectiveProviderId)
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
                      disabled={assignMutation.isPending}
                      onChange={(event) =>
                        assignMutation.mutate({
                          capability: assignment.capability,
                          providerId: event.target.value === '' ? null : event.target.value,
                        })
                      }
                      sx={{ minWidth: 200 }}
                    >
                      <MenuItem value="">
                        <em>Platform default</em>
                      </MenuItem>
                      {selectable.map((provider) => (
                        <MenuItem
                          key={provider.id}
                          value={provider.id}
                          disabled={!provider.defaultModelId}
                        >
                          {provider.displayName}
                          {!provider.defaultModelId && (
                            <Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 1 }}>
                              — set a default model first
                            </Typography>
                          )}
                        </MenuItem>
                      ))}
                      </Select>
                    </FormControl>
                  </TableCell>
                  <TableCell>
                    {effective ? (
                      <Typography variant="body2">
                        {effective}
                        {assignment.providerId === null && (
                          <Typography component="span" variant="caption" color="text.secondary">
                            {' '}
                            (via platform default)
                          </Typography>
                        )}
                      </Typography>
                    ) : (
                      <Typography variant="body2" color="error">
                        Nothing can serve this yet
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
