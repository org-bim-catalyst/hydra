import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  FormHelperText,
  InputLabel,
  ListItemText,
  MenuItem,
  Paper,
  Select,
  Snackbar,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../../../api/httpClient'
import type { AnnouncementAudience, AnnouncementKind, PublishedAnnouncement } from '../api/adminNotificationsApi'
import { getRoles } from '../api/adminRolesApi'
import { AdminShell } from '../components/AdminShell'
import { zodResolver } from '../forms/zodResolver'
import { useCanManageNotifications, useNotificationAnnouncements, usePublishNotificationAnnouncement } from '../hooks/useAdminNotifications'

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.'

const KIND_LABEL: Record<AnnouncementKind, string> = {
  Maintenance: 'Maintenance',
  ServiceDegradation: 'Service degradation',
  ImportantAnnouncement: 'Important announcement',
}

const NO_HTML_OR_LINKS = /<[^>]*>|https?:\/\/|www\./i

/**
 * The same rules the server applies (FR-004a): plain text only, a bounded length, roles when the audience is roles, and an end time
 * in the future. The server stays the authority; this only saves a round trip.
 */
const announcementSchema = z
  .object({
    kind: z.enum(['Maintenance', 'ServiceDegradation', 'ImportantAnnouncement']),
    title: z.string().trim().min(1, 'Enter a title.').max(150, 'The title can be at most 150 characters.').refine((t) => !NO_HTML_OR_LINKS.test(t), 'Links and HTML are not allowed.'),
    message: z.string().trim().min(1, 'Enter a message.').max(2000, 'The message can be at most 2,000 characters.').refine((m) => !NO_HTML_OR_LINKS.test(m), 'Links and HTML are not allowed.'),
    audience: z.enum(['AllActiveUsers', 'Roles']),
    targetRoleIds: z.array(z.string()),
    isCritical: z.boolean(),
    endsAtLocal: z.string().refine((v) => v === '' || new Date(v).getTime() > Date.now(), 'The end time must be in the future.'),
  })
  .refine((v) => v.audience !== 'Roles' || v.targetRoleIds.length > 0, { path: ['targetRoleIds'], message: 'Choose at least one role.' })

type AnnouncementForm = z.infer<typeof announcementSchema>

const DEFAULTS: AnnouncementForm = {
  kind: 'Maintenance',
  title: '',
  message: '',
  audience: 'AllActiveUsers',
  targetRoleIds: [],
  isCritical: false,
  endsAtLocal: '',
}

function PublishDialog({ onClose, onPublished }: { onClose: () => void; onPublished: (result: PublishedAnnouncement) => void }) {
  const publish = usePublishNotificationAnnouncement()
  const [confirming, setConfirming] = useState<AnnouncementForm | null>(null)
  const { control, handleSubmit, formState } = useForm<AnnouncementForm>({
    resolver: zodResolver(announcementSchema),
    defaultValues: DEFAULTS,
    mode: 'onTouched',
  })
  const audience = useWatch({ control, name: 'audience' })
  const roles = useQuery({ queryKey: ['admin', 'notifications', 'role-options'], queryFn: () => getRoles({ pageSize: 100 }), enabled: audience === 'Roles' })

  const send = (values: AnnouncementForm) =>
    publish.mutate(
      {
        kind: values.kind as AnnouncementKind,
        title: values.title,
        message: values.message,
        audience: values.audience as AnnouncementAudience,
        targetRoleIds: values.audience === 'Roles' ? values.targetRoleIds : null,
        isCritical: values.isCritical,
        endsAtUtc: values.endsAtLocal ? new Date(values.endsAtLocal).toISOString() : null,
      },
      { onSuccess: onPublished },
    )

  // A critical announcement also goes out by email to everyone who keeps system email on, so it asks twice.
  const onSubmit = (values: AnnouncementForm) => (values.isCritical && confirming === null ? setConfirming(values) : send(values))

  return (
    <Dialog open onClose={publish.isPending ? undefined : onClose} fullWidth maxWidth="sm" aria-labelledby="publish-announcement-title">
      <DialogTitle id="publish-announcement-title">{confirming ? 'Send this to everyone?' : 'New announcement'}</DialogTitle>
      <DialogContent>
        {confirming ? (
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Alert severity="warning">
              This is a critical announcement. It reaches {confirming.audience === 'Roles' ? 'everyone in the chosen roles' : 'every active user'} in the app{' '}
              <strong>and by email</strong>. Once published it can't be edited or taken back.
            </Alert>
            <Typography variant="subtitle2">{confirming.title}</Typography>
            <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
              {confirming.message}
            </Typography>
          </Stack>
        ) : (
          <Stack spacing={2} sx={{ pt: 1 }} component="form" id="announcement-form" noValidate onSubmit={(e) => void handleSubmit(onSubmit)(e)}>
            <Controller
              name="kind"
              control={control}
              render={({ field }) => (
                <TextField select label="Kind" size="small" {...field}>
                  {Object.entries(KIND_LABEL).map(([value, label]) => (
                    <MenuItem key={value} value={value}>
                      {label}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
            <Controller
              name="title"
              control={control}
              render={({ field, fieldState }) => (
                <TextField label="Title" size="small" required error={!!fieldState.error} helperText={fieldState.error?.message ?? 'Plain text, no links.'} {...field} />
              )}
            />
            <Controller
              name="message"
              control={control}
              render={({ field, fieldState }) => (
                <TextField label="Message" size="small" required multiline minRows={3} error={!!fieldState.error} helperText={fieldState.error?.message ?? 'Plain text, no links.'} {...field} />
              )}
            />
            <Controller
              name="audience"
              control={control}
              render={({ field }) => (
                <TextField select label="Audience" size="small" {...field}>
                  <MenuItem value="AllActiveUsers">All active users</MenuItem>
                  <MenuItem value="Roles">Specific roles</MenuItem>
                </TextField>
              )}
            />
            {audience === 'Roles' && (
              <Controller
                name="targetRoleIds"
                control={control}
                render={({ field, fieldState }) => (
                  <FormControl size="small" error={!!fieldState.error}>
                    <InputLabel id="roles-label">Roles</InputLabel>
                    <Select
                      labelId="roles-label"
                      label="Roles"
                      multiple
                      value={field.value}
                      onChange={field.onChange}
                      renderValue={(ids) => (roles.data?.items ?? []).filter((r) => ids.includes(r.id)).map((r) => r.name).join(', ')}
                    >
                      {(roles.data?.items ?? []).map((r) => (
                        <MenuItem key={r.id} value={r.id}>
                          <Checkbox checked={field.value.includes(r.id)} />
                          <ListItemText primary={r.name} />
                        </MenuItem>
                      ))}
                    </Select>
                    <FormHelperText>
                      {roles.isError ? `The roles couldn't be loaded. ${errorMessage(roles.error)}` : (fieldState.error?.message ?? ' ')}
                    </FormHelperText>
                  </FormControl>
                )}
              />
            )}
            <Controller
              name="endsAtLocal"
              control={control}
              render={({ field, fieldState }) => (
                <TextField
                  label="Ends (optional)"
                  type="datetime-local"
                  size="small"
                  error={!!fieldState.error}
                  helperText={fieldState.error?.message ?? 'After this time the announcement, and any email still waiting, are withdrawn.'}
                  slotProps={{ inputLabel: { shrink: true } }}
                  {...field}
                />
              )}
            />
            <Controller
              name="isCritical"
              control={control}
              render={({ field }) => (
                <FormControlLabel control={<Switch checked={field.value} onChange={(_, checked) => field.onChange(checked)} />} label="Critical: also send by email" />
              )}
            />
            {formState.isSubmitted && !formState.isValid && <Alert severity="error">Fix the highlighted fields to continue.</Alert>}
          </Stack>
        )}
        {publish.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {`The announcement wasn't published. ${errorMessage(publish.error)}`}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        {confirming ? (
          <>
            <Button onClick={() => setConfirming(null)} disabled={publish.isPending}>
              Back
            </Button>
            <Button variant="contained" color="warning" onClick={() => send(confirming)} disabled={publish.isPending}>
              {publish.isPending ? 'Publishing…' : 'Publish to everyone'}
            </Button>
          </>
        ) : (
          <>
            <Button onClick={onClose} disabled={publish.isPending}>
              Cancel
            </Button>
            <Button type="submit" form="announcement-form" variant="contained" disabled={publish.isPending}>
              {publish.isPending ? 'Publishing…' : 'Publish'}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  )
}

/**
 * specs/067 US6 (FR-004a) — system announcements. They are immutable once published, so this page has no edit or delete, and a
 * critical one asks for a second confirmation because it also goes out by email.
 */
export function AdminAnnouncementsPage() {
  const canManage = useCanManageNotifications()
  const announcements = useNotificationAnnouncements()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [published, setPublished] = useState<PublishedAnnouncement | null>(null)
  const items = announcements.data?.pages.flatMap((p) => p.items) ?? []

  return (
    <AdminShell
      title="Announcements"
      subtitle="Messages to everyone, or to chosen roles"
      actions={canManage ? <Button variant="contained" onClick={() => setDialogOpen(true)}>New announcement</Button> : undefined}
    >
      <Paper elevation={1} sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
        {announcements.isError && (
          <Alert severity="error" action={<Button color="inherit" size="small" onClick={() => void announcements.refetch()}>Retry</Button>}>
            {errorMessage(announcements.error)}
          </Alert>
        )}
        <TableContainer>
          <Table size="small" aria-label="Announcements">
            <TableHead>
              <TableRow>
                <TableCell>Title</TableCell>
                <TableCell>Kind</TableCell>
                <TableCell>Audience</TableCell>
                <TableCell>Published</TableCell>
                <TableCell>Ends</TableCell>
                <TableCell align="right">Recipients</TableCell>
                <TableCell>Email</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((a) => (
                <TableRow key={a.id}>
                  <TableCell>
                    <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                      <span>{a.title}</span>
                      {a.isCritical && <Chip size="small" color="warning" label="Critical" />}
                    </Stack>
                  </TableCell>
                  <TableCell>{KIND_LABEL[a.kind]}</TableCell>
                  <TableCell>{a.audience === 'AllActiveUsers' ? 'All active users' : a.targetRoles.map((r) => r.name).join(', ')}</TableCell>
                  <TableCell>
                    {new Date(a.publishedAtUtc).toLocaleString()}
                    <Typography variant="caption" color="text.secondary" component="div">
                      by {a.publishedBy}
                    </Typography>
                  </TableCell>
                  <TableCell>{a.endsAtUtc ? new Date(a.endsAtUtc).toLocaleString() : '—'}</TableCell>
                  <TableCell align="right">
                    {a.fanOutStatus === 'InProgress' ? <Chip size="small" label="Sending…" /> : (a.recipientCount ?? 0)}
                  </TableCell>
                  <TableCell>{a.isCritical ? `${a.emailSent} sent · ${a.emailQueued} queued · ${a.emailExpired} expired` : 'In-app only'}</TableCell>
                </TableRow>
              ))}
              {!announcements.isLoading && items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7}>
                    <Typography variant="body2" color="text.secondary">
                      No announcements yet.
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
        {announcements.isLoading && <CircularProgress size={24} aria-label="Loading announcements" />}
        {announcements.hasNextPage && (
          <Box>
            <Button onClick={() => void announcements.fetchNextPage()} disabled={announcements.isFetchingNextPage}>
              Load more
            </Button>
          </Box>
        )}
      </Paper>

      {dialogOpen && (
        <PublishDialog
          onClose={() => setDialogOpen(false)}
          onPublished={(result) => {
            setDialogOpen(false)
            setPublished(result)
          }}
        />
      )}

      <Snackbar open={published !== null} autoHideDuration={12000} onClose={() => setPublished(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        {published ? (
          <Alert severity="success" variant="filled" onClose={() => setPublished(null)}>
            {`Published. It reaches about ${published.estimatedRecipients} ${published.estimatedRecipients === 1 ? 'person' : 'people'}${
              published.emailEstimatedMinutes > 0 ? `, and the emails take about ${published.emailEstimatedMinutes} min to go out` : ''
            }.`}
          </Alert>
        ) : undefined}
      </Snackbar>
    </AdminShell>
  )
}
