import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  FormControl,
  IconButton,
  InputLabel,
  Link,
  MenuItem,
  Paper,
  Select,
  Snackbar,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import AddIcon from '@mui/icons-material/Add'
import PlayArrowIcon from '@mui/icons-material/PlayArrow'
import StopIcon from '@mui/icons-material/Stop'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link as RouterLink } from 'react-router'
import { ApiError } from '../../../api/httpClient'
import { SUPPORTED_LANGUAGES } from '../../chat/languageOptions'
import * as adminVoiceApi from '../api/adminVoiceApi'
import type { AdminVoiceProvider } from '../api/adminVoiceApi'
import { AddVoiceProviderDialog } from '../components/AddVoiceProviderDialog'
import { AdminShell } from '../components/AdminShell'
import { VoiceProviderCredentialDialog } from '../components/VoiceProviderCredentialDialog'
import { useSampleAudioPlayer } from '../hooks/useSampleAudioPlayer'

/** Mirrors `PreviewVoiceCommandValidator.MaxTextLength`. */
const MAX_SAMPLE_LENGTH = 500

const SAMPLE_SENTENCES: Record<string, string> = {
  en: "Hello, I'm Lucy. How can I help you today?",
  ar: 'مرحبا، أنا لوسي. كيف يمكنني مساعدتك اليوم؟',
  es: 'Hola, soy Lucy. ¿En qué puedo ayudarte hoy?',
  fr: "Bonjour, je suis Lucy. Comment puis-je vous aider aujourd'hui ?",
  de: 'Hallo, ich bin Lucy. Wie kann ich Ihnen heute helfen?',
}

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.'

/**
 * specs/070 — Lucy's voice. Pick a provider, pick one of its voices, audition it on a sample
 * sentence, and make it the voice Lucy speaks with. The providers not chosen stay behind it as
 * failovers, in the order they were added.
 */
export function AdminVoicePage() {
  const queryClient = useQueryClient()
  const player = useSampleAudioPlayer()

  const [chosenProviderId, setChosenProviderId] = useState<string | null>(null)
  const [voiceChoice, setVoiceChoice] = useState<{ providerId: string; voiceId: string } | null>(null)
  const [language, setLanguage] = useState('en')
  const [sampleText, setSampleText] = useState(SAMPLE_SENTENCES.en)
  const [addDialogOpen, setAddDialogOpen] = useState(false)
  const [credentialProvider, setCredentialProvider] = useState<AdminVoiceProvider | null>(null)
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(null)

  const providersQuery = useQuery({
    queryKey: adminVoiceApi.VOICE_QUERY_KEYS.providers,
    queryFn: adminVoiceApi.getVoiceProviders,
  })
  const providers = providersQuery.data ?? []

  // Until the administrator picks one, show Lucy's current voice.
  const provider =
    providers.find((p) => p.id === chosenProviderId) ?? providers.find((p) => p.isPrimary) ?? providers[0] ?? null

  const voicesQuery = useQuery({
    queryKey: adminVoiceApi.VOICE_QUERY_KEYS.voices(provider?.id ?? ''),
    queryFn: () => adminVoiceApi.getVoiceProviderVoices(provider!.id),
    enabled: provider !== null,
  })
  const voices = voicesQuery.data ?? []

  const chosenVoiceId = voiceChoice?.providerId === provider?.id ? voiceChoice?.voiceId : undefined
  const voiceId =
    voices.find((v) => v.id === chosenVoiceId)?.id ??
    voices.find((v) => v.id === provider?.defaultVoiceId)?.id ??
    voices[0]?.id ??
    ''

  const isLucysVoice = provider !== null && provider.isPrimary && provider.defaultVoiceId === voiceId

  const previewMutation = useMutation({
    mutationFn: () => adminVoiceApi.previewVoice(provider!.id, voiceId, sampleText.trim(), language),
    onSuccess: (preview) => player.play(preview.audioBase64, preview.contentType),
  })

  const setPrimaryMutation = useMutation({
    mutationFn: () => adminVoiceApi.setPrimaryVoiceProvider(provider!.id, voiceId),
    onSuccess: (updated) => {
      queryClient.setQueryData(adminVoiceApi.VOICE_QUERY_KEYS.providers, updated)
      const voiceName = voices.find((v) => v.id === voiceId)?.name ?? voiceId
      setFeedback({ severity: 'success', message: `Lucy now speaks with ${provider!.displayName} — ${voiceName}.` })
    },
    onError: (err: unknown) => setFeedback({ severity: 'error', message: errorMessage(err) }),
  })

  const selectProvider = (id: string) => {
    player.stop()
    previewMutation.reset()
    setChosenProviderId(id)
  }

  const changeLanguage = (next: string) => {
    // Swap the sentence only while it is still the untouched sample for the old language.
    if (sampleText === SAMPLE_SENTENCES[language]) {
      setSampleText(SAMPLE_SENTENCES[next] ?? sampleText)
    }
    setLanguage(next)
  }

  const handleProviderSaved = (saved: AdminVoiceProvider) => {
    void queryClient.invalidateQueries({ queryKey: adminVoiceApi.VOICE_QUERY_KEYS.providers })
    void queryClient.invalidateQueries({ queryKey: adminVoiceApi.VOICE_QUERY_KEYS.voices(saved.id) })
  }

  const handleProviderAdded = (added: AdminVoiceProvider) => {
    handleProviderSaved(added)
    void queryClient.invalidateQueries({ queryKey: adminVoiceApi.VOICE_QUERY_KEYS.engines })
    selectProvider(added.id)
    setFeedback({ severity: 'success', message: `${added.displayName} added.` })
  }

  const canPreview = provider !== null && voiceId !== '' && sampleText.trim() !== ''

  return (
    <AdminShell title="Voice" subtitle="Choose the text-to-speech provider and voice Lucy speaks with">
      <Paper elevation={1} sx={{ p: 3, maxWidth: 760 }}>
        {providersQuery.isError && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={
              <Button color="inherit" size="small" onClick={() => void providersQuery.refetch()}>
                Retry
              </Button>
            }
          >
            {errorMessage(providersQuery.error)}
          </Alert>
        )}

        {providersQuery.isSuccess && providers.length === 0 && (
          <Alert severity="info" sx={{ mb: 2 }}>
            No voice provider has been added yet. Add one with the + button.
          </Alert>
        )}

        <Stack spacing={3}>
          {/* Row 1 — the provider. */}
          <Box>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <FormControl fullWidth disabled={providersQuery.isLoading || providers.length === 0}>
                <InputLabel id="voice-provider-label">Voice provider</InputLabel>
                <Select
                  labelId="voice-provider-label"
                  label="Voice provider"
                  value={provider?.id ?? ''}
                  onChange={(event) => selectProvider(event.target.value)}
                >
                  {providers.map((p) => (
                    <MenuItem key={p.id} value={p.id}>
                      {p.displayName}
                      {p.isPrimary ? " — Lucy's voice" : ` — failover ${p.priority}`}
                      {p.modelStatus === 'ModelUnavailable' ? ' (model unavailable)' : ''}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
              <Tooltip title="Add voice provider">
                <IconButton aria-label="Add voice provider" onClick={() => setAddDialogOpen(true)}>
                  <AddIcon />
                </IconButton>
              </Tooltip>
            </Stack>
            {provider?.vendorEnabled != null && (
              // Keyed and switched under AI providers, where the same key also serves its health
              // check, model list and live dictation — so there is no key to set here.
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1 }}>
                <Chip
                  size="small"
                  variant="outlined"
                  color={provider.vendorEnabled ? 'success' : 'default'}
                  label={provider.vendorEnabled ? 'On' : 'Off'}
                />
                <Typography variant="body2" color="text.secondary">
                  {provider.vendorEnabled
                    ? `API key: ${provider.credentialHint ?? 'not set'}.`
                    : 'Switched off — replies fail over to the next voice provider.'}{' '}
                  <Link component={RouterLink} to="/admin/ai-providers">
                    Manage under AI providers
                  </Link>
                </Typography>
              </Stack>
            )}
            {provider?.requiresCredential && provider.vendorEnabled == null && (
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1 }}>
                <Typography variant="body2" color="text.secondary">
                  API key: {provider.credentialHint ?? 'not set — using the server configuration, if any'}
                </Typography>
                <Button size="small" onClick={() => setCredentialProvider(provider)}>
                  {provider.hasCredential ? 'Replace key' : 'Set key'}
                </Button>
              </Stack>
            )}
            {provider?.modelStatus === 'ModelUnavailable' && (
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1 }}>
                {/* specs/072 FR-037 — the row is kept; replies fail over until the model is back. */}
                <Tooltip title={provider.modelStatusReason ?? ''} describeChild>
                  <Chip size="small" color="warning" variant="outlined" label="Model unavailable" />
                </Tooltip>
                <Typography variant="body2" color="text.secondary">
                  Replies fail over to the next voice provider.
                </Typography>
              </Stack>
            )}
          </Box>

          {/* Row 2 — the voice. */}
          <Box>
            <FormControl fullWidth disabled={provider === null || voicesQuery.isLoading || voices.length === 0}>
              <InputLabel id="voice-label">Voice</InputLabel>
              <Select
                labelId="voice-label"
                label="Voice"
                value={voiceId}
                onChange={(event) => provider && setVoiceChoice({ providerId: provider.id, voiceId: event.target.value })}
              >
                {voices.map((v) => (
                  <MenuItem key={v.id} value={v.id}>
                    {v.name}
                    {v.description ? ` — ${v.description}` : ''}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
            {voicesQuery.isLoading && provider !== null && (
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1 }}>
                <CircularProgress size={14} aria-hidden />
                <Typography variant="body2" color="text.secondary">
                  Loading voices…
                </Typography>
              </Stack>
            )}
            {voicesQuery.isError && (
              <Alert
                severity="error"
                sx={{ mt: 1 }}
                action={
                  <Button color="inherit" size="small" onClick={() => void voicesQuery.refetch()}>
                    Retry
                  </Button>
                }
              >
                {errorMessage(voicesQuery.error)}
              </Alert>
            )}
            {voicesQuery.isSuccess && voices.length === 0 && (
              <Alert severity="warning" sx={{ mt: 1 }}>
                {provider?.displayName} offers no voices.
              </Alert>
            )}
          </Box>

          {/* Row 3 — the audition. */}
          <Box>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'flex-start' } }}>
              <FormControl sx={{ minWidth: 140 }}>
                <InputLabel id="sample-language-label">Language</InputLabel>
                <Select
                  labelId="sample-language-label"
                  label="Language"
                  value={language}
                  onChange={(event) => changeLanguage(event.target.value)}
                >
                  {SUPPORTED_LANGUAGES.map((option) => (
                    <MenuItem key={option.code} value={option.code}>
                      {option.label}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
              <TextField
                label="Sample sentence"
                fullWidth
                multiline
                maxRows={4}
                value={sampleText}
                onChange={(event) => setSampleText(event.target.value)}
                slotProps={{ htmlInput: { maxLength: MAX_SAMPLE_LENGTH, dir: 'auto' } }}
              />
              {player.isPlaying ? (
                <Button variant="outlined" startIcon={<StopIcon />} onClick={player.stop} sx={{ minWidth: 110, height: 56 }}>
                  Stop
                </Button>
              ) : (
                <Button
                  variant="outlined"
                  startIcon={previewMutation.isPending ? <CircularProgress size={16} /> : <PlayArrowIcon />}
                  disabled={!canPreview || previewMutation.isPending}
                  onClick={() => {
                    player.clearError()
                    previewMutation.mutate()
                  }}
                  sx={{ minWidth: 110, height: 56 }}
                >
                  {previewMutation.isPending ? 'Speaking…' : 'Play'}
                </Button>
              )}
            </Stack>
            {previewMutation.isError && (
              <Alert severity="error" sx={{ mt: 1 }}>
                {errorMessage(previewMutation.error)}
              </Alert>
            )}
            {player.error && (
              <Alert severity="error" sx={{ mt: 1 }} onClose={player.clearError}>
                {player.error}
              </Alert>
            )}
          </Box>

          <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
            {isLucysVoice ? (
              <Chip color="success" variant="outlined" label="Lucy's voice" />
            ) : (
              <Button
                variant="contained"
                disabled={provider === null || voiceId === '' || setPrimaryMutation.isPending}
                onClick={() => setPrimaryMutation.mutate()}
              >
                {setPrimaryMutation.isPending ? 'Saving…' : "Set as Lucy's voice"}
              </Button>
            )}
            {provider && !provider.isPrimary && (
              <Typography variant="body2" color="text.secondary">
                Lucy currently speaks with {providers.find((p) => p.isPrimary)?.displayName ?? 'no provider'}.
              </Typography>
            )}
          </Stack>
        </Stack>
      </Paper>

      <AddVoiceProviderDialog open={addDialogOpen} onClose={() => setAddDialogOpen(false)} onAdded={handleProviderAdded} />
      <VoiceProviderCredentialDialog
        provider={credentialProvider}
        onClose={() => setCredentialProvider(null)}
        onSaved={(saved) => {
          handleProviderSaved(saved)
          setFeedback({ severity: 'success', message: `${saved.displayName} API key saved.` })
        }}
      />

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </AdminShell>
  )
}
