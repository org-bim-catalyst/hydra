import { useMemo, useState } from 'react'
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
import { useFormat, useT } from '../../../i18n/useT'
import type { AnnouncementAudience, AnnouncementKind, PublishedAnnouncement } from '../api/adminNotificationsApi'
import { getRoles } from '../api/adminRolesApi'
import { AdminShell } from '../components/AdminShell'
import { zodResolver } from '../forms/zodResolver'
import { useCanManageNotifications, useNotificationAnnouncements, usePublishNotificationAnnouncement } from '../hooks/useAdminNotifications'
import { useOuterT } from '../hooks/useOuterT'
import { DATE_TIME, errorText, PLAIN_NUMBER, type NotificationAdminT } from '../notificationAdminText'

const KINDS: AnnouncementKind[] = ['Maintenance', 'ServiceDegradation', 'ImportantAnnouncement']

const NO_HTML_OR_LINKS = /<[^>]*>|https?:\/\/|www\./i

/**
 * The same rules the server applies (FR-004a): plain text only, a bounded length, roles when the audience is roles, and an end time
 * in the future. The server stays the authority; this only saves a round trip.
 */
const buildAnnouncementSchema = (t: NotificationAdminT) =>
  z
  .object({
    kind: z.enum(['Maintenance', 'ServiceDegradation', 'ImportantAnnouncement']),
    title: z.string().trim().min(1, t('announcements.validation.titleRequired')).max(150, t('announcements.validation.titleMax')).refine((value) => !NO_HTML_OR_LINKS.test(value), t('announcements.validation.noLinks')),
    message: z.string().trim().min(1, t('announcements.validation.messageRequired')).max(2000, t('announcements.validation.messageMax')).refine((value) => !NO_HTML_OR_LINKS.test(value), t('announcements.validation.noLinks')),
    audience: z.enum(['AllActiveUsers', 'Roles']),
    targetRoleIds: z.array(z.string()),
    isCritical: z.boolean(),
    endsAtLocal: z.string().refine((v) => v === '' || new Date(v).getTime() > Date.now(), t('announcements.validation.endsFuture')),
  })
  .refine((v) => v.audience !== 'Roles' || v.targetRoleIds.length > 0, { path: ['targetRoleIds'], message: t('announcements.validation.rolesRequired') })

type AnnouncementForm = z.infer<ReturnType<typeof buildAnnouncementSchema>>

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
  const t = useT('admin.notifications')
  const schema = useMemo(() => buildAnnouncementSchema(t), [t])
  const publish = usePublishNotificationAnnouncement()
  const [confirming, setConfirming] = useState<AnnouncementForm | null>(null)
  const { control, handleSubmit, formState } = useForm<AnnouncementForm>({
    resolver: zodResolver(schema),
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
      <DialogTitle id="publish-announcement-title">{confirming ? t('announcements.dialog.titleConfirm') : t('announcements.dialog.titleNew')}</DialogTitle>
      <DialogContent>
        {confirming ? (
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Alert severity="warning">
              {t('announcements.dialog.criticalLead', {
                audience: t(confirming.audience === 'Roles' ? 'announcements.dialog.reachRoles' : 'announcements.dialog.reachAll'),
              })}{' '}
              <strong>{t('announcements.dialog.criticalEmphasis')}</strong>
              {t('announcements.dialog.criticalTail')}
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
                <TextField select label={t('announcements.dialog.kind')} size="small" {...field}>
                  {KINDS.map((kind) => (
                    <MenuItem key={kind} value={kind}>
                      {t(`announcements.kinds.${kind}`)}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
            <Controller
              name="title"
              control={control}
              render={({ field, fieldState }) => (
                <TextField label={t('announcements.dialog.title')} size="small" required error={!!fieldState.error} helperText={fieldState.error?.message ?? t('announcements.dialog.plainText')} {...field} />
              )}
            />
            <Controller
              name="message"
              control={control}
              render={({ field, fieldState }) => (
                <TextField label={t('announcements.dialog.message')} size="small" required multiline minRows={3} error={!!fieldState.error} helperText={fieldState.error?.message ?? t('announcements.dialog.plainText')} {...field} />
              )}
            />
            <Controller
              name="audience"
              control={control}
              render={({ field }) => (
                <TextField select label={t('announcements.dialog.audience')} size="small" {...field}>
                  <MenuItem value="AllActiveUsers">{t('announcements.audiences.AllActiveUsers')}</MenuItem>
                  <MenuItem value="Roles">{t('announcements.audiences.Roles')}</MenuItem>
                </TextField>
              )}
            />
            {audience === 'Roles' && (
              <Controller
                name="targetRoleIds"
                control={control}
                render={({ field, fieldState }) => (
                  <FormControl size="small" error={!!fieldState.error}>
                    <InputLabel id="roles-label">{t('announcements.dialog.roles')}</InputLabel>
                    <Select
                      labelId="roles-label"
                      label={t('announcements.dialog.roles')}
                      multiple
                      value={field.value}
                      onChange={field.onChange}
                      renderValue={(ids) =>
                        (roles.data?.items ?? [])
                          .filter((r) => ids.includes(r.id))
                          .map((r) => r.name)
                          .join(t('deliveries.filters.listSeparator'))
                      }
                    >
                      {(roles.data?.items ?? []).map((r) => (
                        <MenuItem key={r.id} value={r.id}>
                          <Checkbox checked={field.value.includes(r.id)} />
                          <ListItemText primary={r.name} />
                        </MenuItem>
                      ))}
                    </Select>
                    <FormHelperText>
                      {roles.isError
                        ? t('announcements.dialog.rolesLoadFailed', { detail: errorText(t, roles.error) })
                        : (fieldState.error?.message ?? ' ')}
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
                  label={t('announcements.dialog.ends')}
                  type="datetime-local"
                  size="small"
                  error={!!fieldState.error}
                  helperText={fieldState.error?.message ?? t('announcements.dialog.endsHelp')}
                  slotProps={{ inputLabel: { shrink: true } }}
                  {...field}
                />
              )}
            />
            <Controller
              name="isCritical"
              control={control}
              render={({ field }) => (
                <FormControlLabel control={<Switch checked={field.value} onChange={(_, checked) => field.onChange(checked)} />} label={t('announcements.dialog.critical')} />
              )}
            />
            {formState.isSubmitted && !formState.isValid && <Alert severity="error">{t('announcements.dialog.fixFields')}</Alert>}
          </Stack>
        )}
        {publish.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {t('announcements.dialog.publishFailed', { detail: errorText(t, publish.error) })}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        {confirming ? (
          <>
            <Button onClick={() => setConfirming(null)} disabled={publish.isPending}>
              {t('announcements.dialog.back')}
            </Button>
            <Button variant="contained" color="warning" onClick={() => send(confirming)} disabled={publish.isPending}>
              {publish.isPending ? t('announcements.dialog.publishing') : t('announcements.dialog.publishEveryone')}
            </Button>
          </>
        ) : (
          <>
            <Button onClick={onClose} disabled={publish.isPending}>
              {t('announcements.dialog.cancel')}
            </Button>
            <Button type="submit" form="announcement-form" variant="contained" disabled={publish.isPending}>
              {publish.isPending ? t('announcements.dialog.publishing') : t('announcements.dialog.publish')}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  )
}

function AnnouncementsContent({
  dialogOpen,
  onCloseDialog,
}: {
  dialogOpen: boolean
  onCloseDialog: () => void
}) {
  const t = useT('admin.notifications')
  const format = useFormat()
  const announcements = useNotificationAnnouncements()
  const [published, setPublished] = useState<PublishedAnnouncement | null>(null)
  const items = announcements.data?.pages.flatMap((p) => p.items) ?? []
  const when = (iso: string) => format.date(iso, DATE_TIME)
  const publishedText = (result: PublishedAnnouncement) =>
    t(result.emailEstimatedMinutes > 0 ? 'announcements.publishedWithEmail' : 'announcements.published', {
      count: result.estimatedRecipients,
      minutes: format.number(result.emailEstimatedMinutes, PLAIN_NUMBER),
    })

  return (
    <>
      <Paper elevation={1} sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
        {announcements.isError && (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => void announcements.refetch()}>
                {t('actions.retry')}
              </Button>
            }
          >
            {errorText(t, announcements.error)}
          </Alert>
        )}
        <TableContainer>
          <Table size="small" aria-label={t('announcements.table.aria')}>
            <TableHead>
              <TableRow>
                <TableCell>{t('announcements.table.title')}</TableCell>
                <TableCell>{t('announcements.table.kind')}</TableCell>
                <TableCell>{t('announcements.table.audience')}</TableCell>
                <TableCell>{t('announcements.table.published')}</TableCell>
                <TableCell>{t('announcements.table.ends')}</TableCell>
                <TableCell sx={{ textAlign: 'end' }}>{t('announcements.table.recipients')}</TableCell>
                <TableCell>{t('announcements.table.email')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((a) => (
                <TableRow key={a.id}>
                  <TableCell>
                    <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                      <span>{a.title}</span>
                      {a.isCritical && <Chip size="small" color="warning" label={t('announcements.table.critical')} />}
                    </Stack>
                  </TableCell>
                  <TableCell>{t(`announcements.kinds.${a.kind}`)}</TableCell>
                  <TableCell>
                    {a.audience === 'AllActiveUsers'
                      ? t('announcements.audiences.AllActiveUsers')
                      : a.targetRoles.map((r) => r.name).join(t('deliveries.filters.listSeparator'))}
                  </TableCell>
                  <TableCell>
                    {when(a.publishedAtUtc)}
                    <Typography variant="caption" color="text.secondary" component="div">
                      {t('announcements.table.publishedBy', { name: a.publishedBy })}
                    </Typography>
                  </TableCell>
                  <TableCell>{a.endsAtUtc ? when(a.endsAtUtc) : '—'}</TableCell>
                  <TableCell sx={{ textAlign: 'end' }}>
                    {a.fanOutStatus === 'InProgress' ? (
                      <Chip size="small" label={t('announcements.table.sending')} />
                    ) : (
                      format.number(a.recipientCount ?? 0, PLAIN_NUMBER)
                    )}
                  </TableCell>
                  <TableCell>
                    {a.isCritical
                      ? t('announcements.table.emailSummary', {
                          sent: format.number(a.emailSent, PLAIN_NUMBER),
                          queued: format.number(a.emailQueued, PLAIN_NUMBER),
                          expired: format.number(a.emailExpired, PLAIN_NUMBER),
                        })
                      : t('announcements.table.inAppOnly')}
                  </TableCell>
                </TableRow>
              ))}
              {!announcements.isLoading && items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7}>
                    <Typography variant="body2" color="text.secondary">
                      {t('announcements.table.empty')}
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
        {announcements.isLoading && <CircularProgress size={24} aria-label={t('announcements.table.loading')} />}
        {announcements.hasNextPage && (
          <Box>
            <Button onClick={() => void announcements.fetchNextPage()} disabled={announcements.isFetchingNextPage}>
              {t('actions.loadMore')}
            </Button>
          </Box>
        )}
      </Paper>

      {dialogOpen && (
        <PublishDialog
          onClose={onCloseDialog}
          onPublished={(result) => {
            onCloseDialog()
            setPublished(result)
          }}
        />
      )}

      <Snackbar open={published !== null} autoHideDuration={12000} onClose={() => setPublished(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        {published ? (
          <Alert severity="success" variant="filled" closeText={t('actions.close')} onClose={() => setPublished(null)}>
            {publishedText(published)}
          </Alert>
        ) : undefined}
      </Snackbar>
    </>
  )
}

/**
 * specs/067 US6 (FR-004a) — system announcements. They are immutable once published, so this page has no edit or delete, and a
 * critical one asks for a second confirmation because it also goes out by email.
 */
export function AdminAnnouncementsPage() {
  const t = useOuterT('admin.notifications')
  const canManage = useCanManageNotifications()
  const [dialogOpen, setDialogOpen] = useState(false)

  return (
    <AdminShell
      title={t('announcements.title')}
      subtitle={t('announcements.subtitle')}
      actions={canManage ? <Button variant="contained" onClick={() => setDialogOpen(true)}>{t('announcements.newAnnouncement')}</Button> : undefined}
    >
      <AnnouncementsContent dialogOpen={dialogOpen} onCloseDialog={() => setDialogOpen(false)} />
    </AdminShell>
  )
}
