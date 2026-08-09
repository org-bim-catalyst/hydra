import { Avatar, Box, Button, Divider, Paper, Stack, TextField } from '@mui/material'
import { useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { AppShell } from '../../../components/AppShell'
import { useMyProfile, useUpdateProfile, useUploadAvatar } from '../hooks/useProfile'

interface ProfileFormValues {
  firstName: string
  lastName: string
}

export function ProfilePage() {
  const { data: profile } = useMyProfile()
  const updateProfile = useUpdateProfile()
  const uploadAvatar = useUploadAvatar()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [avatarUrl, setAvatarUrl] = useState<string | null>(null)

  const { register, handleSubmit } = useForm<ProfileFormValues>({
    values: { firstName: profile?.firstName ?? '', lastName: profile?.lastName ?? '' },
  })

  const handleAvatarChange = async (file: File) => {
    const url = await uploadAvatar.mutateAsync(file)
    setAvatarUrl(url)
  }

  const onSubmit = handleSubmit((values) => updateProfile.mutate(values))

  return (
    <AppShell title="Your profile" subtitle="Manage your account details and avatar.">
      <Box sx={{ display: 'flex', justifyContent: 'center' }}>
        <Box sx={{ maxWidth: 480, width: '100%' }}>
          <Paper elevation={1} sx={{ p: { xs: 3, sm: 4 }, width: '100%', height: 'fit-content' }}>
            <Stack direction="row" spacing={2} sx={{ alignItems: 'center', mb: 3 }}>
              <Avatar src={avatarUrl ?? undefined} sx={{ width: 64, height: 64 }} />
              <input
                ref={fileInputRef}
                type="file"
                accept="image/*"
                hidden
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  if (file) void handleAvatarChange(file)
                }}
              />
              <Button variant="outlined" size="small" onClick={() => fileInputRef.current?.click()}>
                Change avatar
              </Button>
            </Stack>

            <Divider sx={{ mb: 3 }} />

            <Box component="form" onSubmit={onSubmit}>
              <Stack spacing={2.5}>
                <TextField label="Email" value={profile?.email ?? ''} disabled fullWidth />
                <TextField label="First name" fullWidth {...register('firstName')} />
                <TextField label="Last name" fullWidth {...register('lastName')} />
                <Button
                  type="submit"
                  variant="contained"
                  size="large"
                  disabled={updateProfile.isPending}
                >
                  Save changes
                </Button>
              </Stack>
            </Box>
          </Paper>
        </Box>
      </Box>
    </AppShell>
  )
}
