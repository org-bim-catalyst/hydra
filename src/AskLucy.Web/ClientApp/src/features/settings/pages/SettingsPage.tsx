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
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Paper,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material'
import { type ReactNode, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { API_BASE_URL } from '../../../api/httpClient'
import { PageHeader } from '../../../components/PageHeader'
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

function TabPanel({ value, index, children }: { value: number; index: number; children: ReactNode }) {
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
            {changePassword.isError && <Alert severity="error">Could not change password. Check your current password.</Alert>}
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
            <Button type="submit" variant="contained" disabled={changePassword.isPending} sx={{ alignSelf: 'flex-start' }}>
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
                <Typography key={code} variant="body2" sx={{ fontFamily: 'monospace' }}>
                  {code}
                </Typography>
              ))}
            </Stack>
          </Paper>
        )}

        <Stack direction="row" spacing={1.5}>
          {profile?.twoFactorEnabled ? (
            <Button variant="outlined" color="error" onClick={() => disableTwoFactor.mutate()} disabled={disableTwoFactor.isPending}>
              Disable 2FA
            </Button>
          ) : (
            <Button variant="outlined" onClick={() => enableTwoFactor.mutate()} disabled={enableTwoFactor.isPending}>
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
    window.location.assign(`${API_BASE_URL}/auth/external/${provider}/link?ticket=${encodeURIComponent(ticket)}`)
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
            {requestEmailChange.isError && <Alert severity="error">Could not request email change.</Alert>}
            {requestEmailChange.isSuccess && (
              <Alert severity="success">Check your new inbox for a confirmation link.</Alert>
            )}
            <TextField label="New email" type="email" fullWidth {...emailForm.register('newEmail', { required: true })} />
            <Button type="submit" variant="contained" disabled={requestEmailChange.isPending} sx={{ alignSelf: 'flex-start' }}>
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
                    onClick={() => removeExternalLogin.mutate({ provider: login.provider, providerKey: login.providerKey })}
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
            (provider) => !externalLogins?.some((login) => login.provider.toLowerCase() === provider),
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
            This permanently deletes your account, conversations, and files. Enter your password to confirm.
          </DialogContentText>
          {deleteAccount.isError && <Alert severity="error" sx={{ mb: 2 }}>Incorrect password.</Alert>}
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
          <Button color="error" onClick={handleDelete} disabled={!password || deleteAccount.isPending}>
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
          Applies to every new conversation you start — it never changes a conversation already
          in progress.
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
          <Typography variant="body2" color="text.secondary">
            No AI providers are enabled yet — an administrator needs to configure one first.
          </Typography>
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

export function SettingsPage() {
  const [tab, setTab] = useState(0)

  return (
    <Box sx={{ p: { xs: 2, sm: 4 }, bgcolor: 'background.default', minHeight: '100%' }}>
      <PageHeader backTo="/chat" backLabel="Back to chat" title="Settings" />
      <Paper elevation={1} sx={{ maxWidth: 720 }}>
        <Tabs value={tab} onChange={(_, value: number) => setTab(value)} sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}>
          <Tab label="Security" />
          <Tab label="Account" />
          <Tab label="AI Providers" />
          <Tab label="Data" />
          <Tab label="Cookies" />
        </Tabs>
        <Box sx={{ p: 3 }}>
          <TabPanel value={tab} index={0}>
            <SecurityTab />
          </TabPanel>
          <TabPanel value={tab} index={1}>
            <AccountTab />
          </TabPanel>
          <TabPanel value={tab} index={2}>
            <AiProvidersTab />
          </TabPanel>
          <TabPanel value={tab} index={3}>
            <DataTab />
          </TabPanel>
          <TabPanel value={tab} index={4}>
            <CookiePreferencesPanel />
          </TabPanel>
        </Box>
      </Paper>
    </Box>
  )
}
