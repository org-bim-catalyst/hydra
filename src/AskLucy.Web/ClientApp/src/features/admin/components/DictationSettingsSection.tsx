import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  FormControl,
  FormHelperText,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Snackbar,
  Stack,
  Typography,
} from '@mui/material'
import MicIcon from '@mui/icons-material/Mic'
import StopIcon from '@mui/icons-material/Stop'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import { useCan } from '../../auth/hooks/usePermissions'
import { SUPPORTED_LANGUAGES } from '../../chat/languageOptions'
import * as adminVoiceApi from '../api/adminVoiceApi'
import type { DictationPrimaryEngine, LocalWhisperTryResult, PushToTalkEngine } from '../api/adminVoiceApi'
import { MAX_SAMPLE_SECONDS, useWavSampleRecorder } from '../hooks/useWavSampleRecorder'
import { useAudioInputDevices } from '../hooks/useAudioInputDevices'
import { AudioLevelMeter } from './AudioLevelMeter'

/** The Select's value for "whatever the browser/OS treats as default". */
const DEFAULT_DEVICE = ''

/** The Select's value for "no model"; a real id is never empty. */
const NO_MODEL = ''

const PRIMARY_ENGINE_LABELS: Record<DictationPrimaryEngine, string> = {
  LocalWhisper: 'Local Whisper',
  OpenAiWhisper: 'OpenAI Whisper',
  ElevenLabsRealtime: 'ElevenLabs realtime',
}

const PUSH_TO_TALK_ENGINE_LABELS: Record<PushToTalkEngine, string> = {
  LocalWhisper: 'Local Whisper',
  OpenAiWhisper: 'OpenAI Whisper',
  Browser: 'Browser built-in',
}

/** `suspension.engine`/`lastRevert.from` are plain strings on the wire; fall back to the raw value for an unknown one. */
const engineLabel = (engine: string) => PRIMARY_ENGINE_LABELS[engine as DictationPrimaryEngine] ?? engine

const errorMessage = (err: unknown) =>
  err instanceof ApiError
    ? err.detail ?? err.message
    : err instanceof Error
      ? err.message
      : 'Something went wrong. Please try again.'

/**
 * specs/078 contracts/admin-dictation.md — the dictation half of Admin → Voice. Picks the Custom
 * Models deployment Local Whisper uses, says why it isn't serving when it isn't, and lets the
 * administrator try a deployment on a recorded sample before selecting it (FR-009a, FR-009c).
 */
export function DictationSettingsSection() {
  const queryClient = useQueryClient()
  const canManage = useCan('admin.ai-providers.manage')
  const recorder = useWavSampleRecorder()
  const inputDevices = useAudioInputDevices()

  // `undefined` until the administrator picks: then the saved selection shows.
  const [chosenModelId, setChosenModelId] = useState<string | undefined>(undefined)
  const [language, setLanguage] = useState('en')
  const [inputDeviceId, setInputDeviceId] = useState(DEFAULT_DEVICE)
  const [tryResult, setTryResult] = useState<LocalWhisperTryResult | null>(null)
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(null)

  const settingsQuery = useQuery({
    queryKey: adminVoiceApi.VOICE_QUERY_KEYS.dictation,
    queryFn: adminVoiceApi.getDictationSettings,
  })
  const settings = settingsQuery.data
  const localWhisper = settings?.localWhisper
  const savedModelId = localWhisper?.selectedModelId ?? NO_MODEL
  const modelId = chosenModelId ?? savedModelId
  const chosenOption = localWhisper?.models.find((m) => m.id === modelId)

  const showError = (err: unknown) => setFeedback({ severity: 'error', message: errorMessage(err) })

  const selectMutation = useMutation({
    mutationFn: () => adminVoiceApi.selectLocalWhisperModel(modelId || null, settings!.rowVersion),
    onSuccess: async () => {
      setChosenModelId(undefined)
      setFeedback({
        severity: 'success',
        message: chosenOption ? `Local Whisper now uses ${chosenOption.label}.` : 'Local Whisper has no model selected.',
      })
      await queryClient.invalidateQueries({ queryKey: adminVoiceApi.VOICE_QUERY_KEYS.dictation })
    },
    onError: (err: unknown) => {
      showError(err)
      // Someone else changed the setting: show theirs so the next save starts from it.
      if (err instanceof ApiError && err.status === 409) {
        void queryClient.invalidateQueries({ queryKey: adminVoiceApi.VOICE_QUERY_KEYS.dictation })
      }
    },
  })

  const tryMutation = useMutation({
    mutationFn: (wav: Blob) => adminVoiceApi.tryLocalWhisperModel(modelId, wav, language),
    onSuccess: setTryResult,
    onError: showError,
  })

  const primaryEngineMutation = useMutation({
    mutationFn: (engine: DictationPrimaryEngine) => adminVoiceApi.setDictationPrimaryEngine(engine, settings!.rowVersion),
    onSuccess: async (_data, engine) => {
      setFeedback({ severity: 'success', message: `Primary dictation engine is now ${PRIMARY_ENGINE_LABELS[engine]}.` })
      await queryClient.invalidateQueries({ queryKey: adminVoiceApi.VOICE_QUERY_KEYS.dictation })
    },
    onError: (err: unknown) => {
      showError(err)
      if (err instanceof ApiError && err.status === 409) {
        void queryClient.invalidateQueries({ queryKey: adminVoiceApi.VOICE_QUERY_KEYS.dictation })
      }
    },
  })

  const pushToTalkEngineMutation = useMutation({
    mutationFn: (engine: PushToTalkEngine) => adminVoiceApi.setPushToTalkEngine(engine, settings!.rowVersion),
    onSuccess: async () => {
      setFeedback({ severity: 'success', message: 'Push-to-Talk engine updated.' })
      await queryClient.invalidateQueries({ queryKey: adminVoiceApi.VOICE_QUERY_KEYS.dictation })
    },
    onError: (err: unknown) => {
      showError(err)
      if (err instanceof ApiError && err.status === 409) {
        void queryClient.invalidateQueries({ queryKey: adminVoiceApi.VOICE_QUERY_KEYS.dictation })
      }
    },
  })

  const toggleRecording = async () => {
    try {
      if (recorder.isRecording) {
        tryMutation.mutate(await recorder.stop())
      } else {
        setTryResult(null)
        await recorder.start(inputDeviceId || undefined)
      }
    } catch (err) {
      showError(err)
    }
  }

  const chooseModel = (id: string) => {
    setChosenModelId(id)
    setTryResult(null)
  }

  return (
    <Paper elevation={1} sx={{ p: 3, mt: 3, maxWidth: 760 }}>
      <Typography variant="h6" component="h2" gutterBottom>
        Dictation
      </Typography>

      {settingsQuery.isLoading && <CircularProgress size={24} aria-label="Loading dictation settings" />}
      {settingsQuery.isError && (
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => void settingsQuery.refetch()}>
              Retry
            </Button>
          }
        >
          We couldn&apos;t load the dictation settings. {errorMessage(settingsQuery.error)}
        </Alert>
      )}

      {settings && localWhisper && (
        <Stack spacing={2}>
          {settings.state === 'Suspended' && settings.suspension && (
            <Alert severity="warning">
              {engineLabel(settings.suspension.engine)} is suspended: {settings.suspension.reason}. Dictation uses the
              browser built-in until it&apos;s renewed.
            </Alert>
          )}

          {settings.lastRevert && (
            <Alert severity="info">
              {engineLabel(settings.lastRevert.from)} was reverted to Local Whisper on{' '}
              {new Date(settings.lastRevert.atUtc).toLocaleString()} because its vendor was switched off.
            </Alert>
          )}

          <Typography variant="subtitle1" component="h3">
            Primary dictation engine
          </Typography>

          <FormControl size="small" disabled={!canManage || primaryEngineMutation.isPending}>
            <InputLabel id="dictation-primary-engine-label">Primary engine</InputLabel>
            <Select
              labelId="dictation-primary-engine-label"
              label="Primary engine"
              value={settings.primaryEngine}
              onChange={(event) => primaryEngineMutation.mutate(event.target.value as DictationPrimaryEngine)}
            >
              {settings.engines.map((choice) => (
                <MenuItem key={choice.engine} value={choice.engine} disabled={!choice.selectable}>
                  {PRIMARY_ENGINE_LABELS[choice.engine]}
                </MenuItem>
              ))}
            </Select>
            {settings.engines
              .filter((choice) => !choice.selectable && choice.unavailableReason)
              .map((choice) => (
                <FormHelperText key={choice.engine}>
                  {PRIMARY_ENGINE_LABELS[choice.engine]}: {choice.unavailableReason}
                </FormHelperText>
              ))}
          </FormControl>

          {settings.primaryEngine === 'ElevenLabsRealtime' && (
            <FormControl size="small" disabled={!canManage || pushToTalkEngineMutation.isPending}>
              <InputLabel id="dictation-ptt-engine-label">Push-to-Talk engine</InputLabel>
              <Select
                labelId="dictation-ptt-engine-label"
                label="Push-to-Talk engine"
                value={settings.pushToTalkEngine}
                onChange={(event) => pushToTalkEngineMutation.mutate(event.target.value as PushToTalkEngine)}
              >
                {settings.pushToTalkEngines.map((choice) => (
                  <MenuItem key={choice.engine} value={choice.engine} disabled={!choice.selectable}>
                    {PUSH_TO_TALK_ENGINE_LABELS[choice.engine]}
                  </MenuItem>
                ))}
              </Select>
              {settings.pushToTalkEngines
                .filter((choice) => !choice.selectable && choice.unavailableReason)
                .map((choice) => (
                  <FormHelperText key={choice.engine}>
                    {PUSH_TO_TALK_ENGINE_LABELS[choice.engine]}: {choice.unavailableReason}
                  </FormHelperText>
                ))}
            </FormControl>
          )}

          <Typography variant="subtitle1" component="h3">
            Local Whisper model
          </Typography>

          {localWhisper.effectiveModel.problem ? (
            <Alert severity={localWhisper.selectedModelId ? 'warning' : 'info'}>{localWhisper.effectiveModel.problem}</Alert>
          ) : (
            <Typography variant="body2" color="text.secondary">
              Local Whisper uses {localWhisper.effectiveModel.label}.
            </Typography>
          )}

          <FormControl size="small" disabled={!canManage}>
            <InputLabel id="local-whisper-model-label">Model</InputLabel>
            <Select
              labelId="local-whisper-model-label"
              label="Model"
              value={modelId}
              onChange={(event) => chooseModel(event.target.value)}
            >
              <MenuItem value={NO_MODEL}>No model (browser built-in)</MenuItem>
              {localWhisper.models.map((model) => (
                <MenuItem key={model.id} value={model.id} disabled={!model.selectable}>
                  {model.label}
                </MenuItem>
              ))}
            </Select>
            {localWhisper.models.length === 0 && (
              <FormHelperText>No completed Custom Models deployments yet.</FormHelperText>
            )}
          </FormControl>

          {localWhisper.models
            .filter((model) => !model.selectable && model.reason)
            .map((model) => (
              <Typography key={model.id} variant="caption" color="text.secondary">
                {model.label}: {model.reason}
              </Typography>
            ))}

          {canManage && (
            <Box>
              <Button
                variant="contained"
                disabled={modelId === savedModelId || selectMutation.isPending}
                onClick={() => selectMutation.mutate()}
              >
                {selectMutation.isPending ? 'Saving…' : 'Use this model'}
              </Button>
            </Box>
          )}

          {canManage && chosenOption && (
            <Stack spacing={1.5}>
              <Typography variant="subtitle2" component="h4">
                Try {chosenOption.label}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Record up to {MAX_SAMPLE_SECONDS} seconds and hear how this model transcribes it. Trying a model
                doesn&apos;t select it.
              </Typography>
              <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                <FormControl size="small" sx={{ minWidth: 160 }}>
                  <InputLabel id="try-language-label">Language</InputLabel>
                  <Select
                    labelId="try-language-label"
                    label="Language"
                    value={language}
                    onChange={(event) => setLanguage(event.target.value)}
                  >
                    {SUPPORTED_LANGUAGES.map((option) => (
                      <MenuItem key={option.code} value={option.code}>
                        {option.label}
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
                <FormControl size="small" sx={{ minWidth: 220 }} disabled={recorder.isRecording}>
                  <InputLabel id="try-microphone-label">Microphone</InputLabel>
                  <Select
                    labelId="try-microphone-label"
                    label="Microphone"
                    value={inputDeviceId}
                    onChange={(event) => setInputDeviceId(event.target.value)}
                  >
                    <MenuItem value={DEFAULT_DEVICE}>System default</MenuItem>
                    {inputDevices.map((device) => (
                      <MenuItem key={device.deviceId} value={device.deviceId}>
                        {device.label || `Microphone (${device.deviceId.slice(0, 8)})`}
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
              </Stack>
              <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
                <Button
                  variant="outlined"
                  startIcon={recorder.isRecording ? <StopIcon /> : <MicIcon />}
                  disabled={tryMutation.isPending}
                  onClick={() => void toggleRecording()}
                  sx={{ minWidth: 110 }}
                >
                  {recorder.isRecording ? 'Stop' : 'Try it'}
                </Button>
                {tryMutation.isPending && <CircularProgress size={20} aria-label="Transcribing" />}
              </Stack>
              <AudioLevelMeter
                label={`Input: ${recorder.inputDeviceLabel ?? 'Not detected yet'}`}
                level={recorder.inputLevel}
                active={recorder.isRecording}
              />
              {tryResult && (
                <Alert severity="success">
                  <Typography variant="body2">&ldquo;{tryResult.text}&rdquo;</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {tryResult.modelLabel} · {tryResult.elapsedMs} ms
                  </Typography>
                </Alert>
              )}
            </Stack>
          )}
        </Stack>
      )}

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </Paper>
  )
}
