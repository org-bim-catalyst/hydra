import { useState } from 'react'
import { Box, Button, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import { useFormat, useT } from '../../../i18n/useT'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import { useMcpAuditLog } from '../hooks/useMcpServers'

// The same fields `Date#toLocaleString()` shows, so English reads exactly as before.
const DATE_TIME: Intl.DateTimeFormatOptions = {
  year: 'numeric',
  month: 'numeric',
  day: 'numeric',
  hour: 'numeric',
  minute: 'numeric',
  second: 'numeric',
}

/** spec.md FR-058 — cursor-paginated audit trail for one MCP server. */
export function McpAuditLogTable({ serverId }: { serverId: string }) {
  const t = useT('admin.mcpServers')
  const format = useFormat()
  const [cursorStack, setCursorStack] = useState<(string | null)[]>([null])
  const cursor = cursorStack[cursorStack.length - 1]
  const { data, isLoading } = useMcpAuditLog(serverId, cursor)

  return (
    <Box>
      <Typography variant="subtitle1" sx={{ mb: 1 }}>
        {t('audit.title')}
      </Typography>

      <TableContainer component={Paper}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('audit.columns.occurred')}</TableCell>
              <TableCell>{t('audit.columns.action')}</TableCell>
              <TableCell>{t('audit.columns.user')}</TableCell>
              <TableCell>{t('audit.columns.details')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={4} />}
            {!isLoading && (data?.items ?? []).length === 0 && (
              <TableEmptyRow colSpan={4} message={t('audit.empty')} />
            )}
            {(data?.items ?? []).map((entry) => (
              <TableRow key={entry.id}>
                <TableCell>{format.date(entry.occurredAtUtc, DATE_TIME)}</TableCell>
                <TableCell>{entry.action}</TableCell>
                <TableCell>
                  <bdi dir="ltr">{entry.userId}</bdi>
                </TableCell>
                <TableCell sx={{ maxWidth: 320, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  <bdi dir="ltr">{entry.detailsJson}</bdi>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
        <Button size="small" disabled={cursorStack.length <= 1} onClick={() => setCursorStack((s) => s.slice(0, -1))}>
          {t('audit.previous')}
        </Button>
        <Button
          size="small"
          disabled={!data?.nextCursor}
          onClick={() => setCursorStack((s) => [...s, data!.nextCursor])}
        >
          {t('audit.next')}
        </Button>
      </Stack>
    </Box>
  )
}
