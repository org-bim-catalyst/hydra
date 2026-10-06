import { useState } from 'react'
import { Alert, Box, Button, Chip, Paper, Stack, ToggleButton, ToggleButtonGroup, Tooltip, Typography } from '@mui/material'
import { ApiError } from '../../../api/httpClient'
import type { ChannelHealth, NotificationChannelStatus } from '../api/adminNotificationsApi'
import { NotificationSeriesChart } from '../charts/NotificationSeriesChart'
import { AdminShell } from '../components/AdminShell'
import { useNotificationChannels, useNotificationStatistics } from '../hooks/useAdminNotifications'

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.'

const RANGES = [
  { key: '24h', label: '24 hours', hours: 24 },
  { key: '7d', label: '7 days', hours: 24 * 7 },
  { key: '30d', label: '30 days', hours: 24 * 30 },
  { key: '90d', label: '90 days', hours: 24 * 90 },
] as const

const HEALTH_COLOR: Record<ChannelHealth, 'success' | 'warning' | 'error'> = { Healthy: 'success', Degraded: 'warning', Unhealthy: 'error' }

const duration = (ms: number | null) => {
  if (ms === null) return '—'
  if (ms < 1000) return `${ms} ms`
  const seconds = ms / 1000
  return seconds < 120 ? `${seconds.toFixed(1)} s` : `${(seconds / 60).toFixed(1)} min`
}

function StatCard({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <Paper variant="outlined" sx={{ p: 2, minWidth: 150, flex: '1 1 150px' }}>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="h5" component="p">
        {value}
      </Typography>
      {hint && (
        <Typography variant="caption" color="text.secondary">
          {hint}
        </Typography>
      )}
    </Paper>
  )
}

function ChannelChip({ channel }: { channel: NotificationChannelStatus }) {
  const name = channel.channel === 'InApp' ? 'In-app' : 'Email'
  const chip = (
    <Chip
      color={HEALTH_COLOR[channel.health]}
      label={`${name} (${channel.provider}): ${channel.health}${channel.enabled ? '' : ', switched off'}`}
      variant={channel.enabled ? 'filled' : 'outlined'}
    />
  )
  return channel.detail ? <Tooltip title={channel.detail}>{chip}</Tooltip> : chip
}

/**
 * specs/067 US6 — how the notification hub is doing: counts, delivery speed, what is waiting, and whether each channel is up.
 * Everything comes from database aggregates and the cached health checks; no recipient address or message body is shown.
 */
export function AdminNotificationsDashboardPage() {
  const [range, setRange] = useState<(typeof RANGES)[number]['key']>('7d')
  const hours = RANGES.find((r) => r.key === range)!.hours
  // The window is whole hours so the query key doesn't change on every render.
  const [now] = useState(() => new Date().setMinutes(0, 0, 0))
  const window = { from: new Date(now - hours * 3_600_000).toISOString(), to: new Date(now + 3_600_000).toISOString() }

  const statistics = useNotificationStatistics(window)
  const channels = useNotificationChannels()
  const stats = statistics.data

  return (
    <AdminShell title="Notifications" subtitle="Delivery health, volume and what is waiting">
      <Stack spacing={3}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <ToggleButtonGroup
            exclusive
            size="small"
            value={range}
            aria-label="Time range"
            onChange={(_, value: (typeof RANGES)[number]['key'] | null) => value && setRange(value)}
          >
            {RANGES.map((r) => (
              <ToggleButton key={r.key} value={r.key}>
                {r.label}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
        </Stack>

        <Box>
          <Typography variant="subtitle2" sx={{ mb: 1 }}>
            Channels
          </Typography>
          {channels.isError ? (
            <Alert severity="error" action={<Button color="inherit" size="small" onClick={() => void channels.refetch()}>Retry</Button>}>
              {errorMessage(channels.error)}
            </Alert>
          ) : (
            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
              {(channels.data ?? []).map((c) => (
                <ChannelChip key={c.channel} channel={c} />
              ))}
            </Stack>
          )}
        </Box>

        {statistics.isError && (
          <Alert severity="error" action={<Button color="inherit" size="small" onClick={() => void statistics.refetch()}>Retry</Button>}>
            {errorMessage(statistics.error)}
          </Alert>
        )}

        {stats && (
          <>
            <Stack direction="row" useFlexGap spacing={2} sx={{ flexWrap: 'wrap' }}>
              <StatCard label="Created" value={String(stats.created)} />
              <StatCard label="Sent" value={String(stats.sent)} />
              <StatCard label="Failed" value={String(stats.failed)} hint={stats.ambiguous > 0 ? `${stats.ambiguous} with an unknown outcome` : undefined} />
              <StatCard label="Dead-lettered" value={String(stats.deadLettered)} />
              <StatCard label="Retries" value={String(stats.retries)} />
              <StatCard label="Email success" value={stats.emailSuccessRate === null ? '—' : `${(stats.emailSuccessRate * 100).toFixed(1)}%`} />
              <StatCard label="Average delivery" value={duration(stats.averageDeliveryLatencyMs)} />
              <StatCard label="Slowest 5% (p95)" value={duration(stats.p95DeliveryLatencyMs)} />
              <StatCard
                label="Waiting now"
                value={String(stats.backlog.outboxPending + stats.backlog.deliveriesDue)}
                hint={stats.backlog.oldestDueAgeSeconds > 0 ? `oldest ${Math.ceil(stats.backlog.oldestDueAgeSeconds / 60)} min` : undefined}
              />
              <StatCard label="Unread by users" value={String(stats.unreadNotifications)} />
            </Stack>

            <Paper variant="outlined" sx={{ p: 2 }}>
              <NotificationSeriesChart series={stats.series} hourly={hours <= 48} />
            </Paper>

            {stats.byCategory.length > 0 && (
              <Box>
                <Typography variant="subtitle2" sx={{ mb: 1 }}>
                  By category
                </Typography>
                <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
                  {stats.byCategory.map((c) => (
                    <Chip key={c.category} variant="outlined" label={`${c.category}: ${c.created} created, ${c.failed} failed`} />
                  ))}
                </Stack>
              </Box>
            )}
          </>
        )}
      </Stack>
    </AdminShell>
  )
}
