import { useState } from 'react'
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
import { ApiError } from '../../../api/httpClient'
import type {
  AdminDelivery,
  DeliveryFilters,
  DeliveryStatus,
  NotificationCategory,
  NotificationChannel,
  RetryRefusal,
} from '../api/adminNotificationsApi'
import { AdminShell } from '../components/AdminShell'
import {
  useBulkRetryNotificationDeliveries,
  useCanManageNotifications,
  useNotificationDeliveries,
  useNotificationDelivery,
  useRetryNotificationDelivery,
} from '../hooks/useAdminNotifications'

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.'

const STATUSES: DeliveryStatus[] = ['Failed', 'DeadLettered', 'Pending', 'Retrying', 'Sent', 'Skipped', 'Expired', 'Cancelled']
const CATEGORIES: NotificationCategory[] = ['Security', 'Account', 'Agent', 'Workflow', 'Document', 'KnowledgeBase', 'Memory', 'System']
const DEFAULT_STATUSES: DeliveryStatus[] = ['Failed', 'DeadLettered']

const REFUSAL_TEXT: Record<RetryRefusal, string> = {
  NotFailed: "It isn't failed any more.",
  NotificationDeleted: 'The user deleted this notification.',
  NotificationExpired: 'This notification has expired.',
  RecipientDeleted: "The recipient's account no longer exists.",
}

const when = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : '—')

function RetryButton({ delivery, onRetry, busy }: { delivery: AdminDelivery; onRetry: (id: string) => void; busy: boolean }) {
  const button = (
    <span>
      <Button size="small" disabled={!delivery.retryable || busy} onClick={() => onRetry(delivery.deliveryId)}>
        Retry
      </Button>
    </span>
  )
  return delivery.notRetryableReason ? <Tooltip title={REFUSAL_TEXT[delivery.notRetryableReason]}>{button}</Tooltip> : button
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
  const detail = useNotificationDelivery(deliveryId)
  const delivery = detail.data?.delivery

  const rows: [string, string][] = delivery
    ? [
        ['Type', delivery.type],
        ['Channel', delivery.channel],
        ['Status', delivery.status],
        ['Attempts', String(delivery.attempts)],
        ['Last attempt', when(delivery.lastAttemptAtUtc)],
        ['Next attempt', when(delivery.nextAttemptAtUtc)],
        ['Failure', delivery.failureKind ?? '—'],
        ['Reason', delivery.failureReason ?? '—'],
        ['Provider response', delivery.providerResponse ?? '—'],
        ['Recipient', [delivery.recipient.displayName, delivery.recipient.address].filter(Boolean).join(' · ') || delivery.recipient.kind],
        ['Correlation id', delivery.correlationId],
        ['Language', detail.data!.notification.language],
        ['Created', when(detail.data!.notification.createdAtUtc)],
      ]
    : []

  return (
    <Drawer anchor="right" open={deliveryId !== null} onClose={onClose}>
      <Box sx={{ width: { xs: '100vw', sm: 420 }, p: 3 }} role="region" aria-label="Delivery details">
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
          <Typography variant="h6">Delivery</Typography>
          <IconButton aria-label="Close" onClick={onClose}>
            <CloseIcon />
          </IconButton>
        </Stack>
        {detail.isLoading && <CircularProgress size={24} aria-label="Loading the delivery" />}
        {detail.isError && <Alert severity="error">{errorMessage(detail.error)}</Alert>}
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
            {delivery.notRetryableReason && <Alert severity="info">{REFUSAL_TEXT[delivery.notRetryableReason]}</Alert>}
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

/**
 * specs/067 US6 — failed and dead-lettered deliveries, with safe details: addresses are masked and account-email content is never
 * shown. Retrying is for administrators who can manage notifications; for everyone else the controls aren't there at all.
 */
export function AdminNotificationDeliveriesPage() {
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

  const handleRetry = (deliveryId: string) =>
    retry.mutate(deliveryId, {
      onSuccess: () => setToast({ severity: 'success', text: 'The delivery was queued to be sent again.' }),
      onError: (err) => setToast({ severity: 'error', text: `The retry didn't go through. ${errorMessage(err)}` }),
    })

  const handleBulkRetry = () =>
    bulkRetry.mutate(selected, {
      onSuccess: (result) => {
        setSelected([])
        setToast({
          severity: result.skipped.length > 0 ? 'error' : 'success',
          text: `Retried ${result.retried} of ${result.requested}${result.skipped.length > 0 ? `, ${result.skipped.length} skipped` : ''}.`,
        })
      },
      onError: (err) => setToast({ severity: 'error', text: `The retry didn't go through. ${errorMessage(err)}` }),
    })

  const toggle = (id: string) => setSelected((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]))
  const allSelected = retryable.length > 0 && retryable.every((d) => selected.includes(d.deliveryId))

  return (
    <AdminShell title="Deliveries" subtitle="What failed to reach people, and why">
      <Paper elevation={1} sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Stack direction="row" useFlexGap spacing={2} sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
          <FormControl size="small" sx={{ minWidth: 220 }}>
            <InputLabel id="status-filter-label">Status</InputLabel>
            <Select
              labelId="status-filter-label"
              multiple
              label="Status"
              value={statuses}
              onChange={(e) => {
                const value = e.target.value
                setStatuses(typeof value === 'string' ? (value.split(',') as DeliveryStatus[]) : value.length > 0 ? value : DEFAULT_STATUSES)
                setSelected([])
              }}
              renderValue={(value) => value.join(', ')}
            >
              {STATUSES.map((s) => (
                <MenuItem key={s} value={s}>
                  {s}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <FormControl size="small" sx={{ minWidth: 140 }}>
            <InputLabel id="channel-filter-label">Channel</InputLabel>
            <Select labelId="channel-filter-label" label="Channel" value={channel} onChange={(e) => setChannel(e.target.value as NotificationChannel | '')}>
              <MenuItem value="">Any</MenuItem>
              <MenuItem value="Email">Email</MenuItem>
              <MenuItem value="InApp">In-app</MenuItem>
            </Select>
          </FormControl>
          <FormControl size="small" sx={{ minWidth: 160 }}>
            <InputLabel id="category-filter-label">Category</InputLabel>
            <Select labelId="category-filter-label" label="Category" value={category} onChange={(e) => setCategory(e.target.value as NotificationCategory | '')}>
              <MenuItem value="">Any</MenuItem>
              {CATEGORIES.map((c) => (
                <MenuItem key={c} value={c}>
                  {c}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          {canManage && (
            <Button variant="contained" disabled={selected.length === 0 || selected.length > 200 || bulkRetry.isPending} onClick={handleBulkRetry}>
              {selected.length > 0 ? `Retry ${selected.length} selected` : 'Retry selected'}
            </Button>
          )}
        </Stack>

        {deliveries.isError && (
          <Alert severity="error" action={<Button color="inherit" size="small" onClick={() => void deliveries.refetch()}>Retry</Button>}>
            {errorMessage(deliveries.error)}
          </Alert>
        )}

        <TableContainer>
          <Table size="small" aria-label="Deliveries">
            <TableHead>
              <TableRow>
                {canManage && (
                  <TableCell padding="checkbox">
                    <Checkbox
                      checked={allSelected}
                      indeterminate={selected.length > 0 && !allSelected}
                      disabled={retryable.length === 0}
                      onChange={() => setSelected(allSelected ? [] : retryable.slice(0, 200).map((d) => d.deliveryId))}
                      slotProps={{ input: { 'aria-label': 'Select all retryable deliveries' } }}
                    />
                  </TableCell>
                )}
                <TableCell>Type</TableCell>
                <TableCell>Channel</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Recipient</TableCell>
                <TableCell align="right">Attempts</TableCell>
                <TableCell>Last attempt</TableCell>
                <TableCell>Reason</TableCell>
                <TableCell>
                  <Box component="span" sx={visuallyHidden}>
                    Actions
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
                        slotProps={{ input: { 'aria-label': `Select ${d.type} to ${d.recipient.address ?? d.recipient.kind}` } }}
                      />
                    </TableCell>
                  )}
                  <TableCell>
                    <Button size="small" onClick={() => setOpenId(d.deliveryId)} aria-label={`Open ${d.type} delivery`}>
                      {d.type}
                    </Button>
                  </TableCell>
                  <TableCell>{d.channel}</TableCell>
                  <TableCell>
                    <Chip size="small" label={d.status} color={d.status === 'Failed' || d.status === 'DeadLettered' ? 'error' : 'default'} />
                  </TableCell>
                  <TableCell>{d.recipient.address ?? (d.recipient.kind === 'SupportMailbox' ? 'Support mailbox' : d.recipient.displayName ?? '—')}</TableCell>
                  <TableCell align="right">{d.attempts}</TableCell>
                  <TableCell>{when(d.lastAttemptAtUtc)}</TableCell>
                  <TableCell sx={{ maxWidth: 280 }}>{d.failureReason ?? '—'}</TableCell>
                  <TableCell>{canManage && <RetryButton delivery={d} onRetry={handleRetry} busy={retry.isPending} />}</TableCell>
                </TableRow>
              ))}
              {!deliveries.isLoading && items.length === 0 && (
                <TableRow>
                  <TableCell colSpan={canManage ? 9 : 8}>
                    <Typography variant="body2" color="text.secondary">
                      No deliveries match these filters.
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>

        {deliveries.isLoading && <CircularProgress size={24} aria-label="Loading deliveries" />}
        {deliveries.hasNextPage && (
          <Box>
            <Button onClick={() => void deliveries.fetchNextPage()} disabled={deliveries.isFetchingNextPage}>
              Load more
            </Button>
          </Box>
        )}
      </Paper>

      <DeliveryDrawer deliveryId={openId} canManage={canManage} onClose={() => setOpenId(null)} onRetry={handleRetry} retrying={retry.isPending} />

      <Snackbar open={toast !== null} autoHideDuration={8000} onClose={() => setToast(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        {toast ? (
          <Alert severity={toast.severity} variant="filled" onClose={() => setToast(null)}>
            {toast.text}
          </Alert>
        ) : undefined}
      </Snackbar>
    </AdminShell>
  )
}
