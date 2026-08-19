import { useState } from 'react'
import { Box, Button, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material'
import { useMcpAuditLog } from '../hooks/useMcpServers'

/** spec.md FR-058 — cursor-paginated audit trail for one MCP server. */
export function McpAuditLogTable({ serverId }: { serverId: string }) {
  const [cursorStack, setCursorStack] = useState<(string | null)[]>([null])
  const cursor = cursorStack[cursorStack.length - 1]
  const { data, isLoading } = useMcpAuditLog(serverId, cursor)

  return (
    <Box>
      <Typography variant="subtitle1" sx={{ mb: 1 }}>
        Audit log
      </Typography>

      <TableContainer component={Paper}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Occurred</TableCell>
              <TableCell>Action</TableCell>
              <TableCell>User</TableCell>
              <TableCell>Details</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && (
              <TableRow>
                <TableCell colSpan={4}>Loading…</TableCell>
              </TableRow>
            )}
            {!isLoading && (data?.items ?? []).length === 0 && (
              <TableRow>
                <TableCell colSpan={4}>No audit entries yet.</TableCell>
              </TableRow>
            )}
            {(data?.items ?? []).map((entry) => (
              <TableRow key={entry.id}>
                <TableCell>{new Date(entry.occurredAtUtc).toLocaleString()}</TableCell>
                <TableCell>{entry.action}</TableCell>
                <TableCell>{entry.userId}</TableCell>
                <TableCell sx={{ maxWidth: 320, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {entry.detailsJson}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
        <Button size="small" disabled={cursorStack.length <= 1} onClick={() => setCursorStack((s) => s.slice(0, -1))}>
          Previous
        </Button>
        <Button
          size="small"
          disabled={!data?.nextCursor}
          onClick={() => setCursorStack((s) => [...s, data!.nextCursor])}
        >
          Next
        </Button>
      </Stack>
    </Box>
  )
}
