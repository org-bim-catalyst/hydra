import {
  Alert,
  Button,
  Chip,
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
import { TableEmptyRow } from '../../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../../components/TableLoadingRow'
import type { IncidentSummary } from '../../api/adminOperationalFailuresApi'
import { engineLabel, formatWhen, kindLabel, severityColor } from './operationalFailureLabels'

const COLUMNS = 9

interface IncidentTableProps {
  incidents: IncidentSummary[]
  totalCount: number
  /** Zero-based, as MUI's TablePagination counts. */
  page: number
  pageSize: number
  isLoading: boolean
  errorMessage: string | null
  onRetry: () => void
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  onOpen: (incidentId: string) => void
}

/** specs/074 FR-011 — one row per incident, never per occurrence, so a burst reads as one line. */
export function IncidentTable({
  incidents,
  totalCount,
  page,
  pageSize,
  isLoading,
  errorMessage,
  onRetry,
  onPageChange,
  onPageSizeChange,
  onOpen,
}: IncidentTableProps) {
  return (
    <>
      {errorMessage && (
        <Alert
          severity="error"
          sx={{ mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={onRetry}>
              Retry
            </Button>
          }
        >
          {errorMessage}
        </Alert>
      )}
      <TableContainer sx={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
        <Table stickyHeader size="small" aria-label="Operational failure incidents">
          <TableHead>
            <TableRow>
              <TableCell>Severity</TableCell>
              <TableCell>Operation</TableCell>
              <TableCell>Engine</TableCell>
              <TableCell>Provider / model</TableCell>
              <TableCell>Kind</TableCell>
              <TableCell align="right">Occurrences</TableCell>
              <TableCell align="right">Users</TableCell>
              <TableCell>Last seen</TableCell>
              <TableCell>State</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={COLUMNS} />}
            {!isLoading && !errorMessage && incidents.length === 0 && (
              <TableEmptyRow colSpan={COLUMNS} message="No operational failures match this view." />
            )}
            {incidents.map((incident) => (
              <TableRow key={incident.id} hover>
                <TableCell>
                  <Chip size="small" label={incident.severity} color={severityColor(incident.severity)} variant="outlined" />
                </TableCell>
                <TableCell>
                  <Link component="button" variant="body2" onClick={() => onOpen(incident.id)} sx={{ textAlign: 'left' }}>
                    {incident.operation}
                  </Link>
                  {incident.isRecurrence && (
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                      Recurrence
                    </Typography>
                  )}
                </TableCell>
                <TableCell>{engineLabel(incident.engine)}</TableCell>
                <TableCell>
                  {[incident.providerName, incident.model].filter(Boolean).join(' · ') || (
                    <Typography component="span" variant="body2" color="text.secondary">
                      —
                    </Typography>
                  )}
                </TableCell>
                <TableCell>{kindLabel(incident.kind)}</TableCell>
                <TableCell align="right">
                  <span>{incident.occurrenceCount}</span>
                  {incident.storedOccurrenceCount < incident.occurrenceCount && (
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                      showing {incident.storedOccurrenceCount} of {incident.occurrenceCount}
                    </Typography>
                  )}
                  {incident.recoveryCount > 0 && (
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                      recovered {incident.recoveryCount}×
                    </Typography>
                  )}
                </TableCell>
                <TableCell align="right">{incident.distinctUserCount}</TableCell>
                <TableCell>{formatWhen(incident.lastSeenUtc)}</TableCell>
                <TableCell>{incident.state}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <TablePagination
        component="div"
        count={totalCount}
        page={page}
        rowsPerPage={pageSize}
        rowsPerPageOptions={[25, 50, 100]}
        onPageChange={(_, next) => onPageChange(next)}
        onRowsPerPageChange={(event) => onPageSizeChange(Number(event.target.value))}
      />
    </>
  )
}
