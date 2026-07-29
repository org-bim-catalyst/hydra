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
import { useDeleteAccount, useMyProfile } from '../../profile/hooks/useProfile'
import { downloadMyPersonalData } from '../../profile/api/profileApi'

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

export function SettingsPage() {
  const [tab, setTab] = useState(0)

  return (
    <Box sx={{ p: { xs: 2, sm: 4 }, bgcolor: 'background.default', minHeight: '100%' }}>
      <PageHeader backTo="/chat" backLabel="Back to chat" title="Settings" />
      <Paper elevation={1} sx={{ maxWidth: 720 }}>
        <Tabs value={tab} onChange={(_, value: number) => setTab(value)} sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}>
          <Tab label="Security" />
          <Tab label="Account" />
          <Tab label="Data" />
        </Tabs>
        <Box sx={{ p: 3 }}>
          <TabPanel value={tab} index={0}>
            <SecurityTab />
          </TabPanel>
          <TabPanel value={tab} index={1}>
            <AccountTab />
          </TabPanel>
          <TabPanel value={tab} index={2}>
            <DataTab />
          </TabPanel>
        </Box>
      </Paper>
    </Box>
  )
}
