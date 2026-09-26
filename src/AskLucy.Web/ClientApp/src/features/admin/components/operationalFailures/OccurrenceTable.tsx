import { useState } from 'react'
import {
  Alert,
  Button,
  Link,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink } from 'react-router'
import { TableEmptyRow } from '../../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../../components/TableLoadingRow'
import { codeFontFamily } from '../../../../theme/tokens/typography'
import * as operationalFailuresApi from '../../api/adminOperationalFailuresApi'
import type { Occurrence } from '../../api/adminOperationalFailuresApi'
import { chatInvestigationRoute, formatWhen } from './operationalFailureLabels'
import { UserRefLink } from './UserRefLink'

const PAGE_SIZE = 25

/** What the occurrence happened to. Only chats link for now; the other investigations arrive with US4. */
function ItemCell({ incidentId, occurrence }: { incidentId: string; occurrence: Occurrence }) {
  const { chat, workflow, document, agent, mcpServer, jobId } = occurrence
  if (chat) {
    return (
      <Link component={RouterLink} to={chatInvestigationRoute(incidentId, chat.id)} variant="body2">
        {chat.title ?? 'Untitled chat'}
        {chat.deleted ? ' (deleted)' : ''}
      </Link>
    )
  }

  const named =
    (workflow && { label: 'Workflow', name: workflow.name, deleted: workflow.deleted }) ||
    (document && { label: 'Document', name: document.name, deleted: document.deleted }) ||
    (agent && { label: 'Agent', name: agent.name, deleted: agent.deleted }) ||
    (mcpServer && { label: 'MCP server', name: mcpServer.name, deleted: mcpServer.deleted }) ||
    null
  if (named) {
    return (
      <Typography component="span" variant="body2">
        {named.label}: {named.deleted ? 'deleted' : (named.name ?? 'unnamed')}
      </Typography>
    )
  }

  if (jobId) {
    return (
      <Typography component="span" variant="body2">
        Job {jobId}
      </Typography>
    )
  }

  return (
    <Typography component="span" variant="body2" color="text.secondary">
      —
    </Typography>
  )
}

/** specs/074 FR-012 — the stored occurrences behind one incident, newest first. */
export function OccurrenceTable({ incidentId, showSource }: { incidentId: string; showSource: boolean }) {
  const [page, setPage] = useState(0)
  const query = useQuery({
    queryKey: operationalFailuresApi.OPERATIONAL_FAILURE_QUERY_KEYS.occurrences(incidentId, page + 1, PAGE_SIZE),
    queryFn: () => operationalFailuresApi.getOccurrences(incidentId, page + 1, PAGE_SIZE),
  })
  const occurrences = query.data?.items ?? []
  const columns = showSource ? 5 : 4

  return (
    <>
      {query.isError && (
        <Alert
          severity="error"
          sx={{ mb: 1 }}
          action={
            <Button color="inherit" size="small" onClick={() => void query.refetch()}>
              Retry
            </Button>
          }
        >
          Could not load the occurrences.
        </Alert>
      )}
      <TableContainer>
        <Table size="small" aria-label="Occurrences">
          <TableHead>
            <TableRow>
              <TableCell>When</TableCell>
              <TableCell>User</TableCell>
              <TableCell>Item</TableCell>
              {showSource && <TableCell>Source</TableCell>}
              <TableCell>Correlation id</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {query.isLoading && <TableLoadingRow colSpan={columns} rows={3} />}
            {query.isSuccess && occurrences.length === 0 && (
              <TableEmptyRow colSpan={columns} message="No occurrences are stored for this incident." />
            )}
            {occurrences.map((occurrence) => (
              <TableRow key={occurrence.id}>
                <TableCell>{formatWhen(occurrence.occurredAtUtc)}</TableCell>
                <TableCell>
                  {occurrence.user ? (
                    <UserRefLink user={occurrence.user} />
                  ) : (
                    <Typography component="span" variant="body2" color="text.secondary">
                      —
                    </Typography>
                  )}
                </TableCell>
                <TableCell>
                  <ItemCell incidentId={incidentId} occurrence={occurrence} />
                </TableCell>
                {showSource && <TableCell>{occurrence.sourceIp ?? '—'}</TableCell>}
                <TableCell sx={{ fontFamily: codeFontFamily, fontSize: 12 }}>{occurrence.correlationId}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {(query.data?.totalCount ?? 0) > PAGE_SIZE && (
        <TablePagination
          component="div"
          count={query.data?.totalCount ?? 0}
          page={page}
          rowsPerPage={PAGE_SIZE}
          rowsPerPageOptions={[PAGE_SIZE]}
          onPageChange={(_, next) => setPage(next)}
        />
      )}
    </>
  )
}
