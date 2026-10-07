import { useState } from 'react'
import { Alert, Box, Button, Chip, Paper, Stack, ToggleButton, ToggleButtonGroup, Tooltip, Typography } from '@mui/material'
import { useFormat, useT } from '../../../i18n/useT'
import type { ChannelHealth, NotificationChannelStatus } from '../api/adminNotificationsApi'
import { NotificationSeriesChart } from '../charts/NotificationSeriesChart'
import { AdminShell } from '../components/AdminShell'
import { useNotificationChannels, useNotificationStatistics } from '../hooks/useAdminNotifications'
import { useOuterT } from '../hooks/useOuterT'
import {
  channelLabel,
  categoryLabel,
  errorText,
  healthLabel,
  ONE_DECIMAL,
  PLAIN_NUMBER,
} from '../notificationAdminText'

const RANGES = [
  { key: '24h', label: 'h24', hours: 24 },
  { key: '7d', label: 'd7', hours: 24 * 7 },
  { key: '30d', label: 'd30', hours: 24 * 30 },
  { key: '90d', label: 'd90', hours: 24 * 90 },
] as const

const HEALTH_COLOR: Record<ChannelHealth, 'success' | 'warning' | 'error'> = { Healthy: 'success', Degraded: 'warning', Unhealthy: 'error' }

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
  const t = useT('admin.notifications')
  const label = t(channel.enabled ? 'dashboard.channelChip' : 'dashboard.channelChipOff', {
    channel: channelLabel(t, channel.channel),
    provider: channel.provider,
    health: healthLabel(t, channel.health),
  })
  const chip = <Chip color={HEALTH_COLOR[channel.health]} label={label} variant={channel.enabled ? 'filled' : 'outlined'} />
  return channel.detail ? <Tooltip title={channel.detail}>{chip}</Tooltip> : chip
}

function DashboardContent() {
  const t = useT('admin.notifications')
  const format = useFormat()
  const [range, setRange] = useState<(typeof RANGES)[number]['key']>('7d')
  const hours = RANGES.find((r) => r.key === range)!.hours
  // The window is whole hours so the query key doesn't change on every render.
  const [now] = useState(() => new Date().setMinutes(0, 0, 0))
  const window = { from: new Date(now - hours * 3_600_000).toISOString(), to: new Date(now + 3_600_000).toISOString() }

  const statistics = useNotificationStatistics(window)
  const channels = useNotificationChannels()
  const stats = statistics.data

  const count = (value: number) => format.number(value, PLAIN_NUMBER)
  const decimal = (value: number) => format.number(value, ONE_DECIMAL)
  const duration = (ms: number | null) => {
    if (ms === null) return '—'
    if (ms < 1000) return t('dashboard.duration.milliseconds', { value: count(ms) })
    const seconds = ms / 1000
    return seconds < 120
      ? t('dashboard.duration.seconds', { value: decimal(seconds) })
      : t('dashboard.duration.minutes', { value: decimal(seconds / 60) })
  }

  return (
    <Stack spacing={3}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
        <ToggleButtonGroup
          exclusive
          size="small"
          value={range}
          aria-label={t('dashboard.timeRange')}
          onChange={(_, value: (typeof RANGES)[number]['key'] | null) => value && setRange(value)}
        >
          {RANGES.map((r) => (
            <ToggleButton key={r.key} value={r.key}>
              {t(`dashboard.ranges.${r.label}`)}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>
      </Stack>

      <Box>
        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          {t('dashboard.channels')}
        </Typography>
        {channels.isError ? (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => void channels.refetch()}>
                {t('actions.retry')}
              </Button>
            }
          >
            {errorText(t, channels.error)}
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
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => void statistics.refetch()}>
              {t('actions.retry')}
            </Button>
          }
        >
          {errorText(t, statistics.error)}
        </Alert>
      )}

      {stats && (
        <>
          <Stack direction="row" useFlexGap spacing={2} sx={{ flexWrap: 'wrap' }}>
            <StatCard label={t('dashboard.stats.created')} value={count(stats.created)} />
            <StatCard label={t('dashboard.stats.sent')} value={count(stats.sent)} />
            <StatCard
              label={t('dashboard.stats.failed')}
              value={count(stats.failed)}
              hint={stats.ambiguous > 0 ? t('dashboard.stats.ambiguous', { count: count(stats.ambiguous) }) : undefined}
            />
            <StatCard label={t('dashboard.stats.deadLettered')} value={count(stats.deadLettered)} />
            <StatCard label={t('dashboard.stats.retries')} value={count(stats.retries)} />
            <StatCard
              label={t('dashboard.stats.emailSuccess')}
              value={stats.emailSuccessRate === null ? '—' : t('dashboard.percent', { value: decimal(stats.emailSuccessRate * 100) })}
            />
            <StatCard label={t('dashboard.stats.averageDelivery')} value={duration(stats.averageDeliveryLatencyMs)} />
            <StatCard label={t('dashboard.stats.slowest')} value={duration(stats.p95DeliveryLatencyMs)} />
            <StatCard
              label={t('dashboard.stats.waiting')}
              value={count(stats.backlog.outboxPending + stats.backlog.deliveriesDue)}
              hint={
                stats.backlog.oldestDueAgeSeconds > 0
                  ? t('dashboard.stats.oldest', { minutes: count(Math.ceil(stats.backlog.oldestDueAgeSeconds / 60)) })
                  : undefined
              }
            />
            <StatCard label={t('dashboard.stats.unread')} value={count(stats.unreadNotifications)} />
          </Stack>

          <Paper variant="outlined" sx={{ p: 2 }}>
            <NotificationSeriesChart series={stats.series} hourly={hours <= 48} />
          </Paper>

          {stats.byCategory.length > 0 && (
            <Box>
              <Typography variant="subtitle2" sx={{ mb: 1 }}>
                {t('dashboard.byCategory')}
              </Typography>
              <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
                {stats.byCategory.map((c) => (
                  <Chip
                    key={c.category}
                    variant="outlined"
                    label={t('dashboard.categoryChip', {
                      category: categoryLabel(t, c.category),
                      created: count(c.created),
                      failed: count(c.failed),
                    })}
                  />
                ))}
              </Stack>
            </Box>
          )}
        </>
      )}
    </Stack>
  )
}

/**
 * specs/067 US6 — how the notification hub is doing: counts, delivery speed, what is waiting, and whether each channel is up.
 * Everything comes from database aggregates and the cached health checks; no recipient address or message body is shown.
 */
export function AdminNotificationsDashboardPage() {
  const t = useOuterT('admin.notifications')
  return (
    <AdminShell title={t('dashboard.title')} subtitle={t('dashboard.subtitle')}>
      <DashboardContent />
    </AdminShell>
  )
}
