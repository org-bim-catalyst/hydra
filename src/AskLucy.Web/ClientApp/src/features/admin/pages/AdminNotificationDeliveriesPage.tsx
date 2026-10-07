import { useState, type ReactNode } from 'react'
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Divider,
  Drawer,
  FormControl,
  IconButton,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Snackbar,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import CloseIcon from '@mui/icons-material/Close'
import { visuallyHidden } from '@mui/utils'
import { useFormat, useT } from '../../../i18n/useT'
import type {
  AdminDelivery,
  DeliveryFilters,
  DeliveryStatus,
  NotificationCategory,
  NotificationChannel,
} from '../api/adminNotificationsApi'
import { AdminShell } from '../components/AdminShell'
import {
  useBulkRetryNotificationDeliveries,
  useCanManageNotifications,
  useNotificationDeliveries,
  useNotificationDelivery,
  useRetryNotificationDelivery,
} from '../hooks/useAdminNotifications'
import { useOuterT } from '../hooks/useOuterT'
import {
  categoryLabel,
  channelLabel,
  DATE_TIME,
  errorText,
  PLAIN_NUMBER,
  statusLabel,
} from '../notificationAdminText'

const STATUSES: DeliveryStatus[] = ['Failed', 'DeadLettered', 'Pending', 'Retrying', 'Sent', 'Skipped', 'Expired', 'Cancelled']
const CATEGORIES: NotificationCategory[] = ['Security', 'Account', 'Agent', 'Workflow', 'Document', 'KnowledgeBase', 'Memory', 'System']
const DEFAULT_STATUSES: DeliveryStatus[] = ['Failed', 'DeadLettered']

function RetryButton({ delivery, onRetry, busy }: { delivery: AdminDelivery; onRetry: (id: string) => void; busy: boolean }) {
  const t = useT('admin.notifications')
  const button = (
    <span>
      <Button size="small" disabled={!delivery.retryable || busy} onClick={() => onRetry(delivery.deliveryId)}>
        {t('deliveries.retry')}
      </Button>
    </span>
  )
  return delivery.notRetryableReason ? (
    <Tooltip title={t(`deliveries.refusals.${delivery.notRetryableReason}`)}>{button}</Tooltip>
  ) : (
    button
  )
}

function DeliveryDrawer({
  deliveryId,
  canManage,
  onClose,
  onRetry,
  retrying,
}: {
  deliveryId: string | null
  canManage: boolean
  onClose: () => void
  onRetry: (id: string) => void
  retrying: boolean
}) {
  const t = useT('admin.notifications')
  const format = useFormat()
  const detail = useNotificationDelivery(deliveryId)
  const delivery = detail.data?.delivery
  const when = (iso: string | null) => (iso ? format.date(iso, DATE_TIME) : '—')
  // Identifiers, addresses and provider text are data: shown as returned, isolated so they keep their own direction.
  const data = (value: string | null) => (value ? <bdi dir="ltr">{value}</bdi> : '—')

  const rows: [string, ReactNode][] = delivery
    ? [
        [t('deliveries.drawer.type'), data(delivery.type)],
        [t('deliveries.drawer.channel'), channelLabel(t, delivery.channel)],
        [t('deliveries.drawer.status'), statusLabel(t, delivery.status)],
        [t('deliveries.drawer.attempts'), format.number(delivery.attempts, PLAIN_NUMBER)],
        [t('deliveries.drawer.lastAttempt'), when(delivery.lastAttemptAtUtc)],
        [t('deliveries.drawer.nextAttempt'), when(delivery.nextAttemptAtUtc)],
        [t('deliveries.drawer.failure'), data(delivery.failureKind)],
        [t('deliveries.drawer.reason'), delivery.failureReason ?? '—'],
        [t('deliveries.drawer.providerResponse'), delivery.providerResponse ?? '—'],
        [
          t('deliveries.drawer.recipient'),
          data([delivery.recipient.displayName, delivery.recipient.address].filter(Boolean).join(' · ') || delivery.recipient.kind),
        ],
        [t('deliveries.drawer.correlationId'), data(delivery.correlationId)],
        [t('deliveries.drawer.language'), data(detail.data!.notification.language)],
        [t('deliveries.drawer.created'), when(detail.data!.notification.createdAtUtc)],
      ]
    : []

  return (
    <Drawer anchor="right" open={deliveryId !== null} onClose={onClose}>
      <Box sx={{ width: { xs: '100vw', sm: 420 }, p: 3 }} role="region" aria-label={t('deliveries.drawer.region')}>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
          <Typography variant="h6">{t('deliveries.drawer.title')}</Typography>
          <IconButton aria-label={t('deliveries.drawer.close')} onClick={onClose}>
            <CloseIcon />
          </IconButton>
        </Stack>
        {detail.isLoading && <CircularProgress size={24} aria-label={t('deliveries.drawer.loading')} />}
        {detail.isError && <Alert severity="error">{errorText(t, detail.error)}</Alert>}
        {delivery && (
          <Stack spacing={1.5}>
            {detail.data!.notification.title && (
              <Typography variant="subtitle1">{detail.data!.notification.title}</Typography>
            )}
            <Divider />
            {rows.map(([label, value]) => (
              <Box key={label}>
                <Typography variant="caption" color="text.secondary">
                  {label}
                </Typography>
                <Typography variant="body2" sx={{ wordBreak: 'break-word' }}>
                  {value}
                </Typography>
              </Box>
            ))}
            {delivery.notRetryableReason && (
              <Alert severity="info">{t(`deliveries.refusals.${delivery.notRetryableReason}`)}</Alert>
            )}
            {canManage && (
              <Box>
                <RetryButton delivery={delivery} onRetry={onRetry} busy={retrying} />
              </Box>
            )}
          </Stack>
        )}
      </Box>
    </Drawer>
  )
}

function DeliveriesContent() {
  const t = useT('admin.notifications')
  const format = useFormat()
  const canManage = useCanManageNotifications()
  const [statuses, setStatuses] = useState<DeliveryStatus[]>(DEFAULT_STATUSES)
  const [channel, setChannel] = useState<NotificationChannel | ''>('')
  const [category, setCategory] = useState<NotificationCategory | ''>('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [toast, setToast] = useState<{ severity: 'success' | 'error'; text: string } | null>(null)

  const filters: DeliveryFilters = { status: statuses, channel: channel || undefined, category: category || undefined }
  const deliveries = useNotificationDeliveries(filters)
  const retry = useRetryNotificationDelivery()
  const bulkRetry = useBulkRetryNotificationDeliveries()
  const items = deliveries.data?.pages.flatMap((p) => p.items) ?? []
  const retryable = items.filter((d) => d.retryable)
  const when = (iso: string | null) => (iso ? format.date(iso, DATE_TIME) : '—')

  const handleRetry = (deliveryId: string) =>
    retry.mutate(deliveryId, {
      onSuccess: () => setToast({ severity: 'success', text: t('deliveries.toast.retried') }),
      onError: (err) => setToast({ severity: 'error', text: t('deliveries.toast.retryFailed', { detail: errorText(t, err) }) }),
    })

  const handleBulkRetry = () =>
    bulkRetry.mutate(selected, {
      onSuccess: (result) => {
        setSelected([])
        const counts = {
          retried: format.number(result.retried, PLAIN_NUMBER),
          requested: format.number(result.requested, PLAIN_NUMBER),
          skipped: format.number(result.skipped.length, PLAIN_NUMBER),
        }
        setToast({
          severity: result.skipped.length > 0 ? 'error' : 'success',
          text: t(result.skipped.length > 0 ? 'deliveries.toast.bulkResultSkipped' : 'deliveries.toast.bulkResult', counts),
        })
      },
      onError: (err) => setToast({ severity: 'error', text: t('deliveries.toast.retryFailed', { detail: errorText(t, err) }) }),
    })

  const toggle = (id: string) => setSelected((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]))
  const allSelected = retryable.length > 0 && retryable.every((d) => selected.includes(d.deliveryId))

  return (
    <>
      <Paper elevation={1} sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Stack direction="row" useFlexGap spacing={2} sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
          <FormControl size="small" sx={{ minWidth: 220 }}>
            <InputLabel id="status-filter-label">{t('deliveries.filters.status')}</InputLabel>
            <Select
              labelId="status-filter-label"
              multiple
              label={t('deliveries.filters.status')}
              value={statuses}
              onChange={(e) => {
                const value = e.target.value
                setStatuses(typeof value === 'string' ? (value.split(',') as DeliveryStatus[]) : value.length > 0 ? value : DEFAULT_STATUSES)
                setSelected([])
              }}
              renderValue={(value) => value.map((status) => statusLabel(t, status)).join(t('deliveries.filters.listSeparator'))}
            >
              {STATUSES.map((s) => (
                <MenuItem key={s} value={s}>
                  {statusLabel(t, s)}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <FormControl size="small" sx={{ minWidth: 140 }}>
            <InputLabel id="channel-filter-label">{t('deliveries.filters.channel')}</InputLabel>
            <Select
              labelId="channel-filter-label"
              label={t('deliveries.filters.channel')}
              value={channel}
              onChange={(e) => setChannel(e.target.value as NotificationChannel | '')}
            >
              <MenuItem value="">{t('deliveries.filters.any')}</MenuItem>
              <MenuItem value="Email">{channelLabel(t, 'Email')}</MenuItem>
              <MenuItem value="InApp">{channelLabel(t, 'InApp')}</MenuItem>
            </Select>
          </FormControl>
          <FormControl size="small" sx={{ minWidth: 160 }}>
            <InputLabel id="category-filter-label">{t('deliveries.filters.category')}</InputLabel>
            <Select
              labelId="category-filter-label"
              label={t('deliveries.filters.category')}
              value={category}
              onChange={(e) => setCategory(e.target.value as NotificationCategory | '')}
            >
              <MenuItem value="">{t('deliveries.filters.any')}</MenuItem>
              {CATEGORIES.map((c) => (
                <MenuItem key={c} value={c}>
                  {categoryLabel(t, c)}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          {canManage && (
            <Button variant="contained" disabled={selected.length === 0 || selected.length > 200 || bulkRetry.isPending} onClick={handleBulkRetry}>
              {selected.length > 0
                ? t('deliveries.filters.retrySelectedCount', { count: format.number(selected.length, PLAIN_NUMBER) })
                : t('deliveries.filters.retrySelected')}
            </Button>
          )}
        </Stack>

        {deliveries.isError && (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => void deliveries.refetch()}>
                {t('actions.retry')}
              </Button>
            }
          >
            {errorText(t, deliveries.error)}
          </Alert>
        )}

        <TableContainer>
          <Table size="small" aria-label={t('deliveries.table.aria')}>
            <TableHead>
              <TableRow>
                {canManage && (
                  <TableCell padding="checkbox">
                    <Checkbox
                      checked={allSelected}
                      indeterminate={selected.length > 0 && !allSelected}
                      disabled={retryable.length === 0}
                      onChange={() => setSelected(allSelected ? [] : retryable.slice(0, 200).map((d) => d.deliveryId))}
                      slotProps={{ input: { 'aria-label': t('deliveries.table.selectAll') } }}
                    />
                  </TableCell>
                )}
                <TableCell>{t('deliveries.table.columns.type')}</TableCell>
                <TableCell>{t('deliveries.table.columns.channel')}</TableCell>
                <TableCell>{t('deliveries.table.columns.status')}</TableCell>
                <TableCell>{t('deliveries.table.columns.recipient')}</TableCell>
                <TableCell sx={{ textAlign: 'end' }}>{t('deliveries.table.columns.attempts')}</TableCell>
                <TableCell>{t('deliveries.table.columns.lastAttempt')}</TableCell>
                <TableCell>{t('deliveries.table.columns.reason')}</TableCell>
                <TableCell>
                  <Box component="span" sx={visuallyHidden}>
                    {t('deliveries.table.columns.actions')}
                  </Box>
                </TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((d) => (
                <TableRow key={d.deliveryId} hover>
                  {canManage && (
                    <TableCell padding="checkbox">
                      <Checkbox
                        checked={selected.includes(d.deliveryId)}
                        disabled={!d.retryable}
                        onChange={() => toggle(d.deliveryId)}
                        slotProps={{
                          input: {
                            'aria-label': t('deliveries.table.selectRow', { type: d.type, recipient: d.recipient.address ?? d.recipient.kind }),
                          },
                        }}
                      />
                    </TableCell>
                  )}
                  <TableCell>
                    <Button size="small" onClick={() => setOpenId(d.deliveryId)} aria-label={t('deliveries.table.openAria', { type: d.type })}>
                      <bdi dir="ltr">{d.type}</bdi>
                    </Button>
                  </TableCell>
                  <TableCell>{channelLabel(t, d.channel)}</TableCell>
                  <TableCell>
                    <Chip size="small" label={statusLabel(t, d.status)} color={d.status === 'Failed' || d.status === 'DeadLettered' ? 'error' : 'default'} />
                  </TableCell>
                  <TableCell>
                    {d.recipient.address ? (
                      <bdi dir="ltr">{d.recipient.address}</bdi>
                    ) : d.recipient.kind === 'SupportMailbox' ? (
                      t('deliveries.table.supportMailbox')
                    ) : (
                      (d.recipient.displayName ?? '—')
                    )}
                  </TableCell>
                  <TableCell sx={{ textAlign: 'end' }}>{format.number(d.attempts, PLAIN_NUMBER)}</TableCell>
                  <TableCell>{when(d.lastAttemptAtUtc)}</TableCell>
                  <TableCell sx={{ maxWidth: 280 }}>{d.failureReason ?? '—'}</TableCell>
                  <TableCell>{canManage && <RetryButton delivery={d} onRetry={handleRetry} busy={retry.isPending} />}</TableCell>
                </TableRow>
              ))}
              {!deliveries.isLoading && items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={canManage ? 9 : 8}>
                    <Typography variant="body2" color="text.secondary">
                      {t('deliveries.table.empty')}
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>

        {deliveries.isLoading && <CircularProgress size={24} aria-label={t('deliveries.table.loading')} />}
        {deliveries.hasNextPage && (
          <Box>
            <Button onClick={() => void deliveries.fetchNextPage()} disabled={deliveries.isFetchingNextPage}>
              {t('actions.loadMore')}
            </Button>
          </Box>
        )}
      </Paper>

      <DeliveryDrawer deliveryId={openId} canManage={canManage} onClose={() => setOpenId(null)} onRetry={handleRetry} retrying={retry.isPending} />

      <Snackbar open={toast !== null} autoHideDuration={8000} onClose={() => setToast(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        {toast ? (
          <Alert severity={toast.severity} variant="filled" closeText={t('actions.close')} onClose={() => setToast(null)}>
            {toast.text}
          </Alert>
        ) : undefined}
      </Snackbar>
    </>
  )
}

/**
 * specs/067 US6 — failed and dead-lettered deliveries, with safe details: addresses are masked and account-email content is never
 * shown. Retrying is for administrators who can manage notifications; for everyone else the controls aren't there at all.
 */
export function AdminNotificationDeliveriesPage() {
  const t = useOuterT('admin.notifications')
  return (
    <AdminShell title={t('deliveries.title')} subtitle={t('deliveries.subtitle')}>
      <DeliveriesContent />
    </AdminShell>
  )
}
