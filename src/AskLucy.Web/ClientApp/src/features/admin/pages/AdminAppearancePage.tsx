import { useEffect, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  Paper,
  Slider,
  Snackbar,
  Stack,
  Switch,
  Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useBlocker } from 'react-router'
import { ApiError } from '../../../api/httpClient'
import { useCan } from '../../auth/hooks/usePermissions'
import { getPresenceSphereSettings, updatePresenceSphereSettings, type PresenceSphereSettings } from '../../chat/api/presenceSphereApi'
import { PresenceCardFrame } from '../../chat/components/AiPresenceCard'
import { PRESENCE_SPHERE_SETTINGS_KEY } from '../../chat/hooks/usePresenceSphereSettings'
import { DEFAULT_SPHERE_LOOK, SPHERE_LOOK_LIMITS, type PresenceSphereLook } from '../../chat/scene/sphereConstants'
import { AdminShell } from '../components/AdminShell'

/** The preview is idle: it needs no audio. A stable function, so the scene is not handed a new one every render. */
const silent = () => ({ low: 0, mid: 0, high: 0 })

const sameLook = (a: PresenceSphereLook, b: PresenceSphereLook) =>
  a.dotSizeMultiplier === b.dotSizeMultiplier && a.cardFillPercent === b.cardFillPercent && a.zoomEnabled === b.zoomEnabled

const lookOf = ({ dotSizeMultiplier, cardFillPercent, zoomEnabled }: PresenceSphereSettings): PresenceSphereLook => ({
  dotSizeMultiplier,
  cardFillPercent,
  zoomEnabled,
})

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.'

/**
 * specs/080 — where an administrator tunes the presence sphere for everyone: the size of its dots, how much of
 * its card it fills, and whether it can be zoomed. The preview beside the controls is the real card fed with the
 * values on screen, so it changes as a slider moves; nothing reaches anyone else until Save.
 */
export function AdminAppearancePage() {
  const queryClient = useQueryClient()
  const canManage = useCan('admin.appearance.manage')
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(null)
  // What is on screen while it differs from what is saved; null means "the saved values".
  const [draft, setDraft] = useState<PresenceSphereLook | null>(null)

  const query = useQuery({
    queryKey: PRESENCE_SPHERE_SETTINGS_KEY,
    queryFn: getPresenceSphereSettings,
    // Another administrator may have changed them since this browser last asked.
    refetchOnMount: 'always',
  })

  const saved = query.data ? lookOf(query.data) : null
  const look = draft ?? saved ?? DEFAULT_SPHERE_LOOK
  const dirty = draft !== null && saved !== null && !sameLook(draft, saved)

  const save = useMutation({
    mutationFn: updatePresenceSphereSettings,
    onSuccess: (result) => {
      // The chat reads the same cache entry, so this browser's own sphere follows at once.
      queryClient.setQueryData(PRESENCE_SPHERE_SETTINGS_KEY, result)
      setDraft(null)
      setFeedback({ severity: 'success', message: 'Appearance saved. Users see it when they next load the chat.' })
    },
    // constitution VIII: a failed save must reach the user; the values on screen are kept so it can be retried.
    onError: (err: unknown) => setFeedback({ severity: 'error', message: `Not saved. ${errorMessage(err)}` }),
  })

  const change = (patch: Partial<PresenceSphereLook>) => setDraft({ ...look, ...patch })

  // Leaving with unsaved changes loses them (FR-016): in the app, and by closing or reloading the tab.
  const blocker = useBlocker(dirty)
  useEffect(() => {
    if (!dirty) return undefined
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  return (
    <AdminShell
      title="Appearance"
      subtitle="How the presence sphere looks and behaves for everyone. Changes apply when you save, and reach users on their next page load."
    >
      {query.isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
          <CircularProgress aria-label="Loading appearance settings" />
        </Box>
      ) : query.isError ? (
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => void query.refetch()}>
              Retry
            </Button>
          }
        >
          The appearance settings could not be loaded. {errorMessage(query.error)}
        </Alert>
      ) : (
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={4} sx={{ alignItems: { md: 'flex-start' } }}>
          <Paper variant="outlined" sx={{ p: 3, flex: 1, maxWidth: 560 }}>
            <Stack spacing={3}>
              {!canManage && (
                <Alert severity="info">You can see these settings but not change them. Changing them needs the Manage appearance permission.</Alert>
              )}

              <Box>
                <Typography id="dot-size-label" gutterBottom>
                  Dot size: {look.dotSizeMultiplier.toFixed(2)}×
                </Typography>
                <Slider
                  aria-labelledby="dot-size-label"
                  value={look.dotSizeMultiplier}
                  min={SPHERE_LOOK_LIMITS.dotSizeMultiplier.min}
                  max={SPHERE_LOOK_LIMITS.dotSizeMultiplier.max}
                  step={SPHERE_LOOK_LIMITS.dotSizeMultiplier.step}
                  disabled={!canManage}
                  onChange={(_, value) => change({ dotSizeMultiplier: Number(value) })}
                />
                <Typography variant="caption" color="text.secondary">
                  1.00× is the default size. Smaller dots read as a finer, dimmer sphere.
                </Typography>
              </Box>

              <Box>
                <Typography id="card-fill-label" gutterBottom>
                  Size within its card: {look.cardFillPercent}%
                </Typography>
                <Slider
                  aria-labelledby="card-fill-label"
                  value={look.cardFillPercent}
                  min={SPHERE_LOOK_LIMITS.cardFillPercent.min}
                  max={SPHERE_LOOK_LIMITS.cardFillPercent.max}
                  step={SPHERE_LOOK_LIMITS.cardFillPercent.step}
                  disabled={!canManage}
                  onChange={(_, value) => change({ cardFillPercent: Number(value) })}
                />
                <Typography variant="caption" color="text.secondary">
                  How much of the card&apos;s height the sphere fills. The default is 75%.
                </Typography>
              </Box>

              <Box>
                <FormControlLabel
                  control={
                    <Switch checked={look.zoomEnabled} disabled={!canManage} onChange={(_, checked) => change({ zoomEnabled: checked })} />
                  }
                  label="Allow users to zoom the sphere"
                />
                <Typography variant="caption" color="text.secondary" component="p">
                  With scroll or pinch, between a quarter of its size and twice it. Off by default. Dragging to turn it always works.
                </Typography>
              </Box>

              {query.data && (
                <Typography variant="caption" color="text.secondary">
                  {query.data.isDefault
                    ? 'Never changed: the default look is in use.'
                    : `Last changed${query.data.modifiedBy ? ` by ${query.data.modifiedBy}` : ''}${
                        query.data.modifiedAtUtc ? ` on ${new Date(query.data.modifiedAtUtc).toLocaleString()}` : ''
                      }.`}
                </Typography>
              )}

              {canManage && (
                <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
                  <Button variant="contained" disabled={!dirty || save.isPending} onClick={() => save.mutate(look)}>
                    {save.isPending ? 'Saving…' : 'Save'}
                  </Button>
                  <Button disabled={!dirty || save.isPending} onClick={() => setDraft(null)}>
                    Discard changes
                  </Button>
                  <Button
                    disabled={save.isPending || sameLook(look, DEFAULT_SPHERE_LOOK)}
                    onClick={() => setDraft({ ...DEFAULT_SPHERE_LOOK })}
                  >
                    Reset to defaults
                  </Button>
                </Stack>
              )}
            </Stack>
          </Paper>

          <Box>
            <Typography variant="subtitle2" gutterBottom>
              Preview
            </Typography>
            <Box
              data-testid="presence-sphere-preview"
              sx={{ p: 3, borderRadius: 2, bgcolor: (t) => (t.palette.mode === 'dark' ? 'grey.900' : 'grey.200'), display: 'inline-block' }}
            >
              <PresenceCardFrame getFrequencyBands={silent} look={look} floating={false} />
            </Box>
            <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1, maxWidth: 220 }}>
              The same card users see in the chat. It shows the sphere at rest.
            </Typography>
          </Box>
        </Stack>
      )}

      <Dialog open={blocker.state === 'blocked'} onClose={() => blocker.reset?.()}>
        <DialogTitle>Leave without saving?</DialogTitle>
        <DialogContent>
          <DialogContentText>Your changes to the appearance have not been saved. If you leave now they are lost.</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => blocker.reset?.()}>Stay</Button>
          <Button color="error" onClick={() => blocker.proceed?.()}>
            Leave
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar open={feedback !== null} autoHideDuration={6000} onClose={() => setFeedback(null)}>
        {feedback ? (
          <Alert severity={feedback.severity} onClose={() => setFeedback(null)} variant="filled">
            {feedback.message}
          </Alert>
        ) : undefined}
      </Snackbar>
    </AdminShell>
  )
}
