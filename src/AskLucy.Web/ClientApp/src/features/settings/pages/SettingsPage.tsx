import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Divider,
  FormControlLabel,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Paper,
  Slider,
  Stack,
  Switch,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material'
import { type ReactNode, useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useLocation, useNavigate } from 'react-router'
import { API_BASE_URL } from '../../../api/httpClient'
import { AppShell } from '../../../components/AppShell'
import { EmptyState } from '../../../components/EmptyState'
import { codeFontFamily } from '../../../theme/tokens/typography'
import {
  useChangePassword,
  useDisableTwoFactor,
  useEnableTwoFactor,
  useExternalLogins,
  useGenerateRecoveryCodes,
  useIssueExternalLoginLinkTicket,
  useRemoveExternalLogin,
  useRequestEmailChange,
} from '../../auth/hooks/useAuth'
import { useAiModels, useAiProviders } from '../../chat/hooks/useAiCatalog'
import { useDeleteAccount, useMyProfile } from '../../profile/hooks/useProfile'
import { downloadMyPersonalData } from '../../profile/api/profileApi'
import { CookiePreferencesPanel } from '../../consent/components/CookiePreferencesPanel'
import { useAiPreferences, useSaveAiPreferences } from '../hooks/useAiPreferences'
import { useVoicePreferencesStore } from '../../chat/voice/voicePreferencesStore'
import { SETTINGS_TAB_INDEX } from '../settingsTabs'
import { ChatConfigurationTab } from './ChatConfigurationTab'
import { ChatHistoryTab } from './ChatHistoryTab'

function TabPanel({
  value,
  index,
  children,
}: {
  value: number
  index: number
  children: ReactNode
}) {
  if (value !== index) return null
  return <Box sx={{ pt: 3 }}>{children}</Box>
}

function SecurityTab() {
  const { data: profile } = useMyProfile()
  const changePassword = useChangePassword()
  const enableTwoFactor = useEnableTwoFactor()
  const disableTwoFactor = useDisableTwoFactor()
  const generateRecoveryCodes = useGenerateRecoveryCodes()
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)

  const passwordForm = useForm<{ currentPassword: string; newPassword: string }>()
  const onChangePassword = passwordForm.handleSubmit((values) => {
    changePassword.mutate(values, { onSuccess: () => passwordForm.reset() })
  })

  return (
    <Stack spacing={4}>
      <Box>
        <Typography variant="h6" sx={{ mb: 2 }}>
          Change password
        </Typography>
        <Box component="form" onSubmit={onChangePassword} sx={{ maxWidth: 400 }}>
          <Stack spacing={2}>
            {changePassword.isError && (
              <Alert severity="error">
                Could not change password. Check your current password.
              </Alert>
            )}
            {changePassword.isSuccess && <Alert severity="success">Password changed.</Alert>}
            <TextField
              label="Current password"
              type="password"
              fullWidth
              {...passwordForm.register('currentPassword', { required: true })}
            />
            <TextField
              label="New password"
              type="password"
              fullWidth
              helperText="At least 8 characters"
              {...passwordForm.register('newPassword', { required: true, minLength: 8 })}
            />
            <Button
              type="submit"
              variant="contained"
              disabled={changePassword.isPending}
              sx={{ alignSelf: 'flex-start' }}
            >
              Update password
            </Button>
          </Stack>
        </Box>
      </Box>

      <Divider />

      <Box>
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mb: 2 }}>
          <Typography variant="h6">Two-factor authentication</Typography>
          <Chip
            size="small"
            label={profile?.twoFactorEnabled ? 'Enabled' : 'Disabled'}
            color={profile?.twoFactorEnabled ? 'success' : 'default'}
            variant="outlined"
          />
        </Stack>

        {enableTwoFactor.data && (
          <Alert severity="info" sx={{ mb: 2, maxWidth: 480 }}>
            Add this key to your authenticator app: <strong>{enableTwoFactor.data}</strong>
          </Alert>
        )}

        {recoveryCodes && (
          <Paper variant="outlined" sx={{ p: 2, mb: 2, maxWidth: 480 }}>
            <Typography variant="body2" sx={{ mb: 1 }}>
              Save these recovery codes somewhere safe. Each can be used once.
            </Typography>
            <Stack spacing={0.5}>
              {recoveryCodes.map((code) => (
                <Typography key={code} variant="body2" sx={{ fontFamily: codeFontFamily }}>
                  {code}
                </Typography>
              ))}
            </Stack>
          </Paper>
        )}

        <Stack direction="row" spacing={1.5}>
          {profile?.twoFactorEnabled ? (
            <Button
              variant="outlined"
              color="error"
              onClick={() => disableTwoFactor.mutate()}
              disabled={disableTwoFactor.isPending}
            >
              Disable 2FA
            </Button>
          ) : (
            <Button
              variant="outlined"
              onClick={() => enableTwoFactor.mutate()}
              disabled={enableTwoFactor.isPending}
            >
              Enable 2FA
            </Button>
          )}
          <Button
            variant="text"
            onClick={() => generateRecoveryCodes.mutate(undefined, { onSuccess: setRecoveryCodes })}
            disabled={generateRecoveryCodes.isPending}
          >
            Generate recovery codes
          </Button>
        </Stack>
      </Box>
    </Stack>
  )
}

const LINKABLE_PROVIDERS = ['google', 'facebook'] as const

function AccountTab() {
  const { data: profile } = useMyProfile()
  const requestEmailChange = useRequestEmailChange()
  const { data: externalLogins } = useExternalLogins()
  const removeExternalLogin = useRemoveExternalLogin()
  const issueLinkTicket = useIssueExternalLoginLinkTicket()

  const emailForm = useForm<{ newEmail: string }>()
  const onRequestEmailChange = emailForm.handleSubmit((values) => {
    requestEmailChange.mutate(values.newEmail, { onSuccess: () => emailForm.reset() })
  })

  // FR-034: linking is a top-level browser navigation (OAuth redirect can't be an XHR), so we
  // first mint a short-lived link ticket over an authenticated request, then navigate with it.
  const startLink = async (provider: (typeof LINKABLE_PROVIDERS)[number]) => {
    const ticket = await issueLinkTicket.mutateAsync()
    window.location.assign(
      `${API_BASE_URL}/auth/external/${provider}/link?ticket=${encodeURIComponent(ticket)}`,
    )
  }

  return (
    <Stack spacing={4}>
      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          Email address
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Current: {profile?.email}
        </Typography>
        <Box component="form" onSubmit={onRequestEmailChange} sx={{ maxWidth: 400 }}>
          <Stack spacing={2}>
            {requestEmailChange.isError && (
              <Alert severity="error">Could not request email change.</Alert>
            )}
            {requestEmailChange.isSuccess && (
              <Alert severity="success">Check your new inbox for a confirmation link.</Alert>
            )}
            <TextField
              label="New email"
              type="email"
              fullWidth
              {...emailForm.register('newEmail', { required: true })}
            />
            <Button
              type="submit"
              variant="contained"
              disabled={requestEmailChange.isPending}
              sx={{ alignSelf: 'flex-start' }}
            >
              Request email change
            </Button>
          </Stack>
        </Box>
      </Box>

      <Divider />

      <Box>
        <Typography variant="h6" sx={{ mb: 2 }}>
          Connected accounts
        </Typography>
        {removeExternalLogin.isError && (
          <Alert severity="error" sx={{ mb: 2, maxWidth: 480 }}>
            Couldn't remove that sign-in method — you may need a password set first.
          </Alert>
        )}
        {externalLogins && externalLogins.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            No connected accounts.
          </Typography>
        ) : (
          <List sx={{ maxWidth: 480 }}>
            {externalLogins?.map((login) => (
              <ListItem
                key={`${login.provider}-${login.providerKey}`}
                secondaryAction={
                  <Button
                    size="small"
                    color="error"
                    onClick={() =>
                      removeExternalLogin.mutate({
                        provider: login.provider,
                        providerKey: login.providerKey,
                      })
                    }
                    disabled={removeExternalLogin.isPending}
                  >
                    Remove
                  </Button>
                }
              >
                <ListItemText primary={login.displayName} />
              </ListItem>
            ))}
          </List>
        )}
        <Stack direction="row" spacing={1} sx={{ mt: 2 }}>
          {LINKABLE_PROVIDERS.filter(
            (provider) =>
              !externalLogins?.some((login) => login.provider.toLowerCase() === provider),
          ).map((provider) => (
            <Button
              key={provider}
              size="small"
              variant="outlined"
              disabled={issueLinkTicket.isPending}
              onClick={() => startLink(provider)}
            >
              Link {provider === 'google' ? 'Google' : 'Facebook'}
            </Button>
          ))}
        </Stack>
      </Box>
    </Stack>
  )
}

function DataTab() {
  const navigate = useNavigate()
  const deleteAccount = useDeleteAccount()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [password, setPassword] = useState('')

  const handleDelete = () => {
    deleteAccount.mutate(password, {
      onSuccess: () => navigate('/login', { replace: true }),
    })
  }

  return (
    <Stack spacing={4}>
      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          Download your data
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Export your profile information as a JSON file.
        </Typography>
        <Button variant="outlined" onClick={() => void downloadMyPersonalData()}>
          Download personal data
        </Button>
      </Box>

      <Divider />

      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          Delete account
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Permanently delete your account and all associated data. This cannot be undone.
        </Typography>
        <Button variant="outlined" color="error" onClick={() => setConfirmOpen(true)}>
          Delete my account
        </Button>
      </Box>

      <Dialog open={confirmOpen} onClose={() => setConfirmOpen(false)}>
        <DialogTitle>Delete your account?</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            This permanently deletes your account, conversations, and files. Enter your password to
            confirm.
          </DialogContentText>
          {deleteAccount.isError && (
            <Alert severity="error" sx={{ mb: 2 }}>
              Incorrect password.
            </Alert>
          )}
          <TextField
            label="Password"
            type="password"
            fullWidth
            autoFocus
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmOpen(false)}>Cancel</Button>
          <Button
            color="error"
            onClick={handleDelete}
            disabled={!password || deleteAccount.isPending}
          >
            Delete permanently
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  )
}

/**
 * specs/005-multi-provider-ai-engine User Story 3 (FR-017/FR-019) — a saved default
 * provider/model pre-fills every *new* conversation going forward; it never changes an
 * existing conversation already in progress. Follows AccountTab's established shape
 * (local draft state + explicit Save button + inline Alert), not `ProviderModelSelector`'s
 * auto-persist-on-change behavior, which is specific to a live conversation.
 */
export function AiProvidersTab() {
  const { data: preference, isPending: isPreferencePending } = useAiPreferences()
  const { data: providers } = useAiProviders()
  const [draftProviderId, setDraftProviderId] = useState<string | null>(null)
  const { data: models } = useAiModels(draftProviderId)
  const [draftModelId, setDraftModelId] = useState<string | null>(null)
  const savePreferences = useSaveAiPreferences()

  // Seed the draft from the resolved preference once it loads — a real saved choice or the
  // platform fallback, either way a starting point the user can then change (User Story 3,
  // Acceptance Scenario 1). React's sanctioned "adjust state during render" pattern (not an
  // effect, per react-hooks/set-state-in-effect): guarded by `hasSeededDraft` so this only
  // ever fires once, the same render pass `preference` first arrives.
  const [hasSeededDraft, setHasSeededDraft] = useState(false)
  if (preference && !hasSeededDraft) {
    setHasSeededDraft(true)
    setDraftProviderId(preference.defaultProviderId)
    setDraftModelId(preference.defaultModelId)
  }

  const handleProviderChange = (providerId: string) => {
    setDraftProviderId(providerId)
    setDraftModelId(null)
  }

  const handleSave = () => {
    if (!draftProviderId || !draftModelId) return
    savePreferences.mutate({ defaultProviderId: draftProviderId, defaultModelId: draftModelId })
  }

  return (
    <Stack spacing={4}>
      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          Default AI provider &amp; model
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Applies to every new conversation you start — it never changes a conversation already in
          progress.
        </Typography>
        {preference?.isPlatformDefault && (
          <Alert severity="info" sx={{ mb: 2, maxWidth: 480 }}>
            You haven't saved a personal default yet — showing the platform default.
          </Alert>
        )}
        {savePreferences.isError && (
          <Alert severity="error" sx={{ mb: 2, maxWidth: 480 }}>
            Could not save your default provider/model.
          </Alert>
        )}
        {savePreferences.isSuccess && (
          <Alert severity="success" sx={{ mb: 2, maxWidth: 480 }}>
            Default saved.
          </Alert>
        )}
        {!isPreferencePending && providers && providers.length === 0 ? (
          <EmptyState
            title="No AI providers are enabled yet"
            description="An administrator needs to configure one first."
          />
        ) : (
          <Stack direction="row" spacing={2} sx={{ maxWidth: 480 }}>
            <TextField
              select
              label="Provider"
              fullWidth
              value={draftProviderId ?? ''}
              onChange={(e) => handleProviderChange(e.target.value)}
            >
              {(providers ?? []).map((provider) => (
                <MenuItem key={provider.id} value={provider.id}>
                  {provider.displayName}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              label="Model"
              fullWidth
              disabled={!models || models.length === 0}
              value={draftModelId ?? ''}
              onChange={(e) => setDraftModelId(e.target.value)}
            >
              {(models ?? []).map((model) => (
                <MenuItem key={model.id} value={model.id}>
                  {model.displayName}
                </MenuItem>
              ))}
            </TextField>
          </Stack>
        )}
        <Button
          variant="contained"
          sx={{ mt: 2 }}
          disabled={!draftProviderId || !draftModelId || savePreferences.isPending}
          onClick={handleSave}
        >
          Save default
        </Button>
      </Box>
    </Stack>
  )
}

/**
 * spec 012-elevenlabs-voice-engine, contracts/voice-preferences.md (FR-029/FR-030). Every
 * field auto-persists on change via `voicePreferencesStore.update()` — same
 * immediate-persist convention as a live conversation's `ProviderModelSelector`, not
 * AiProvidersTab's explicit-Save-button pattern, since these are lightweight per-field
 * toggles rather than a provider+model pair that only makes sense saved together.
 */
export function VoiceTab() {
  const preferences = useVoicePreferencesStore()
  const [devices, setDevices] = useState<MediaDeviceInfo[]>([])

  useEffect(() => {
    void preferences.hydrateFromServer()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    navigator.mediaDevices
      ?.enumerateDevices()
      .then(setDevices)
      .catch(() => setDevices([]))
  }, [])

  const microphones = devices.filter((d) => d.kind === 'audioinput')
  const speakers = devices.filter((d) => d.kind === 'audiooutput')

  return (
    <Stack spacing={4}>
      {preferences.error && (
        <Alert severity="error" sx={{ maxWidth: 480 }} onClose={preferences.clearError}>
          {preferences.error}
        </Alert>
      )}

      <Box>
        <Typography variant="h6" sx={{ mb: 2 }}>
          Voice conversation
        </Typography>
        <Stack spacing={2} sx={{ maxWidth: 480 }}>
          <TextField
            select
            label="Conversation mode"
            value={preferences.conversationMode}
            onChange={(e) =>
              preferences.update({
                conversationMode: e.target.value as 'PushToTalk' | 'Continuous',
              })
            }
          >
            <MenuItem value="PushToTalk">Push to talk</MenuItem>
            <MenuItem value="Continuous">Continuous</MenuItem>
          </TextField>
          <FormControlLabel
            control={
              <Switch
                checked={preferences.isMuted}
                onChange={(e) => preferences.update({ isMuted: e.target.checked })}
              />
            }
            label="Mute voice output"
          />
        </Stack>
      </Box>

      <Divider />

      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          Advanced
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Overrides the platform default voice for your account. Leave blank to use the default.
        </Typography>
        <Stack spacing={3} sx={{ maxWidth: 480 }}>
          <TextField
            label="Voice ID"
            value={preferences.selectedVoiceId ?? ''}
            onChange={(e) => preferences.update({ selectedVoiceId: e.target.value || null })}
          />
          <Box>
            <Typography variant="body2" gutterBottom>
              Speed
            </Typography>
            <Slider
              aria-label="Speed"
              min={0.5}
              max={2}
              step={0.05}
              value={preferences.voiceSpeed ?? 1}
              onChange={(_, value) => preferences.update({ voiceSpeed: value as number })}
              valueLabelDisplay="auto"
            />
          </Box>
          <Box>
            <Typography variant="body2" gutterBottom>
              Style
            </Typography>
            <Slider
              aria-label="Style"
              min={0}
              max={1}
              step={0.05}
              value={preferences.voiceStyle ?? 0}
              onChange={(_, value) => preferences.update({ voiceStyle: value as number })}
              valueLabelDisplay="auto"
            />
          </Box>
        </Stack>
      </Box>

      <Divider />

      <Box>
        <Typography variant="h6" sx={{ mb: 2 }}>
          Devices
        </Typography>
        <Stack direction="row" spacing={2} sx={{ maxWidth: 480 }}>
          <TextField
            select
            label="Microphone"
            fullWidth
            value={preferences.preferredMicrophoneDeviceId ?? ''}
            onChange={(e) =>
              preferences.update({ preferredMicrophoneDeviceId: e.target.value || null })
            }
          >
            <MenuItem value="">System default</MenuItem>
            {microphones.map((device) => (
              <MenuItem key={device.deviceId} value={device.deviceId}>
                {device.label || 'Microphone'}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            select
            label="Speaker"
            fullWidth
            value={preferences.preferredSpeakerDeviceId ?? ''}
            onChange={(e) =>
              preferences.update({ preferredSpeakerDeviceId: e.target.value || null })
            }
          >
            <MenuItem value="">System default</MenuItem>
            {speakers.map((device) => (
              <MenuItem key={device.deviceId} value={device.deviceId}>
                {device.label || 'Speaker'}
              </MenuItem>
            ))}
          </TextField>
        </Stack>
      </Box>
    </Stack>
  )
}

export function SettingsPage() {
  const location = useLocation()
  // specs/025-chat-configuration-settings, research.md Decision 4 — lets both the account
  // menus and Chat Configuration's own entry-point links land on a specific tab, without
  // introducing per-tab routes.
  const [tab, setTab] = useState(() => (location.state as { tab?: number } | null)?.tab ?? 0)
  // `useState`'s initializer only runs on the very first mount — a navigation to `/settings`
  // while SettingsPage is *already* mounted (e.g. Chat Configuration's own "Go to AI
  // Providers"/"Go to Voice" links, both already on `/settings`) doesn't remount the
  // component, so it wouldn't otherwise pick up the new `location.state.tab`. `location.key`
  // changes on every `navigate()` call, including same-pathname ones, so this re-syncs the
  // active tab each time a caller asks for a specific one (discovered via manual browser
  // verification of quickstart.md — the automated tests only asserted the `navigate()` call
  // itself, not this already-mounted re-render case).
  useEffect(() => {
    const requestedTab = (location.state as { tab?: number } | null)?.tab
    if (requestedTab !== undefined) {
      // Deferred via queueMicrotask (react-hooks/set-state-in-effect): this reacts to
      // location.key — an external navigation event, not a value derived from render — but
      // the rule wants the update in a callback rather than synchronously in the effect body,
      // to avoid a same-commit cascading render.
      queueMicrotask(() => setTab(requestedTab))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.key])

  return (
    <AppShell title="Settings">
      <Paper elevation={1} sx={{ maxWidth: 720 }}>
        <Tabs
          value={tab}
          onChange={(_, value: number) => setTab(value)}
          sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}
        >
          <Tab label="Security" />
          <Tab label="Account" />
          <Tab label="AI Providers" />
          <Tab label="Voice" />
          <Tab label="Chat Configuration" />
          <Tab label="Chat History" />
          <Tab label="Data" />
          <Tab label="Cookies" />
        </Tabs>
        <Box sx={{ p: 3 }}>
          <TabPanel value={tab} index={SETTINGS_TAB_INDEX.Security}>
            <SecurityTab />
          </TabPanel>
          <TabPanel value={tab} index={SETTINGS_TAB_INDEX.Account}>
            <AccountTab />
          </TabPanel>
          <TabPanel value={tab} index={SETTINGS_TAB_INDEX.AiProviders}>
            <AiProvidersTab />
          </TabPanel>
          <TabPanel value={tab} index={SETTINGS_TAB_INDEX.Voice}>
            <VoiceTab />
          </TabPanel>
          <TabPanel value={tab} index={SETTINGS_TAB_INDEX.ChatConfiguration}>
            <ChatConfigurationTab />
          </TabPanel>
          <TabPanel value={tab} index={SETTINGS_TAB_INDEX.ChatHistory}>
            <ChatHistoryTab />
          </TabPanel>
          <TabPanel value={tab} index={SETTINGS_TAB_INDEX.Data}>
            <DataTab />
          </TabPanel>
          <TabPanel value={tab} index={SETTINGS_TAB_INDEX.Cookies}>
            <CookiePreferencesPanel />
          </TabPanel>
        </Box>
      </Paper>
    </AppShell>
  )
}
