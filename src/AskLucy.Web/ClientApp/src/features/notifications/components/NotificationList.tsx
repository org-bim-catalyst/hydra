import { Alert, Box, Button, CircularProgress, Typography } from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useMemo, useRef } from 'react'
import { EmptyState } from '../../../components/EmptyState'
import { useFormat, useT } from '../../../i18n/useT'
import type { NotificationItem as NotificationItemDto } from '../api/notificationsApi'
import { NotificationItem } from './NotificationItem'

interface Row {
  type: 'header' | 'item'
  header?: string
  item?: NotificationItemDto
}

interface DayLabels {
  today: string
  yesterday: string
  date: (date: Date) => string
}

function dayLabel(isoDateUtc: string, now: Date, labels: DayLabels): string {
  const date = new Date(isoDateUtc)
  const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime()
  const diffDays = Math.round((startOfDay(now) - startOfDay(date)) / 86_400_000)
  if (diffDays === 0) return labels.today
  if (diffDays === 1) return labels.yesterday
  return labels.date(date)
}

function groupByDay(items: NotificationItemDto[], now: Date, labels: DayLabels): Row[] {
  const rows: Row[] = []
  let lastHeader: string | null = null
  for (const item of items) {
    const header = dayLabel(item.createdAtUtc, now, labels)
    if (header !== lastHeader) {
      rows.push({ type: 'header', header })
      lastHeader = header
    }
    rows.push({ type: 'item', item })
  }
  return rows
}

export interface NotificationListProps {
  items: NotificationItemDto[]
  isLoading: boolean
  isError: boolean
  onRetry: () => void
  isFetchingNextPage: boolean
  hasNextPage: boolean | undefined
  onFetchNextPage: () => void
  onOpen: (item: NotificationItemDto) => void
}

/**
 * T070 — day-grouped, virtualized (mirrors chat's `VirtualizedChatRows`), with keyboard roving between items.
 * Rendered inside the popover's or the page's `LocalizedSurface`, so it reads the language from there.
 */
export function NotificationList({
  items,
  isLoading,
  isError,
  onRetry,
  isFetchingNextPage,
  hasNextPage,
  onFetchNextPage,
  onOpen,
}: NotificationListProps) {
  const t = useT('notifications')
  const tc = useT('common')
  const format = useFormat()
  const listParentRef = useRef<HTMLDivElement>(null)
  const rows = useMemo(
    () =>
      groupByDay(items, new Date(), {
        today: t('center.today'),
        yesterday: t('center.yesterday'),
        date: (date) => format.date(date, { month: 'long', day: 'numeric', year: 'numeric' }),
      }),
    [items, t, format],
  )
  const itemIndexes = useMemo(() => rows.map((row, index) => (row.type === 'item' ? index : -1)).filter((i) => i >= 0), [rows])

  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => listParentRef.current,
    estimateSize: (index) => (rows[index]?.type === 'header' ? 32 : 72),
    overscan: 10,
  })

  const handleScroll = () => {
    const el = listParentRef.current
    if (!el || isFetchingNextPage || !hasNextPage) return
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 200) {
      onFetchNextPage()
    }
  }

  const focusItem = (fromRowIndex: number, direction: 1 | -1) => {
    const position = itemIndexes.indexOf(fromRowIndex)
    const nextRowIndex = itemIndexes[position + direction]
    if (nextRowIndex === undefined) return
    virtualizer.scrollToIndex(nextRowIndex, { align: 'auto' })
    // The row may not be mounted yet immediately after scrollToIndex; a microtask gives the
    // virtualizer's render a chance to attach the element before we try to focus it.
    queueMicrotask(() => {
      listParentRef.current?.querySelector<HTMLElement>(`[data-index="${nextRowIndex}"] [role="button"]`)?.focus()
    })
  }

  const handleKeyDown = (event: React.KeyboardEvent, rowIndex: number) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      focusItem(rowIndex, 1)
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      focusItem(rowIndex, -1)
    }
  }

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
        <CircularProgress size={24} />
      </Box>
    )
  }

  if (isError) {
    return (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={onRetry}>
            {tc('actions.retry')}
          </Button>
        }
        sx={{ m: 2 }}
      >
        {t('center.loadFailed')}
      </Alert>
    )
  }

  if (items.length === 0) {
    return <EmptyState title={t('center.empty.title')} description={t('center.empty.description')} />
  }

  return (
    <Box
      ref={listParentRef}
      onScroll={handleScroll}
      // Not role="list": the virtualizer only mounts the visible slice of rows at a time, so a
      // real listitem-per-row structure would trip axe's aria-required-children rule against
      // whatever happens to be (un)mounted at assertion time. A labeled region is accessible
      // without asserting a DOM structure the virtualization can't guarantee.
      role="region"
      aria-label={t('center.listLabel')}
      sx={{ overflowY: 'auto', flex: 1, minHeight: 0 }}
    >
      <Box sx={{ position: 'relative', height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((virtualItem) => {
          const row = rows[virtualItem.index]
          return (
            <Box
              key={virtualItem.key}
              data-index={virtualItem.index}
              ref={virtualizer.measureElement}
              onKeyDown={(e) => handleKeyDown(e, virtualItem.index)}
              sx={{ position: 'absolute', top: 0, insetInlineStart: 0, width: '100%', transform: `translateY(${virtualItem.start}px)` }}
            >
              {row.type === 'header' ? (
                <Typography variant="overline" color="text.secondary" sx={{ px: 2, display: 'block' }}>
                  {row.header}
                </Typography>
              ) : (
                <NotificationItem item={row.item!} onOpen={onOpen} />
              )}
            </Box>
          )
        })}
      </Box>
      {isFetchingNextPage && (
        <Typography variant="caption" color="text.secondary" sx={{ px: 2, py: 1, display: 'block' }}>
          {t('center.loadingMore')}
        </Typography>
      )}
    </Box>
  )
}
