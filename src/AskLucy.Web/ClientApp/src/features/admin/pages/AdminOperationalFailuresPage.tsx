import { useState } from 'react'
import { Paper } from '@mui/material'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as operationalFailuresApi from '../api/adminOperationalFailuresApi'
import type { IncidentFilters } from '../api/adminOperationalFailuresApi'
import { AdminShell } from '../components/AdminShell'
import { IncidentDrawer } from '../components/operationalFailures/IncidentDrawer'
import { IncidentFilters as IncidentFilterBar } from '../components/operationalFailures/IncidentFilters'
import { IncidentTable } from '../components/operationalFailures/IncidentTable'
import { useIncidentFilterParams } from '../hooks/useIncidentFilterParams'
import type { IncidentFilterParams } from '../hooks/useIncidentFilterParams'

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : 'Could not load the operational failures.'

/**
 * specs/074 US1 — the administrator's operational failure trail: what failed for users, grouped
 * into incidents, each with the precise reason the user was deliberately not shown and the admin
 * page that fixes it. Opens on unresolved incidents from the last seven days; the filters live in
 * the URL (FR-019).
 */
export function AdminOperationalFailuresPage() {
  const { params, filters: filterValues, update } = useIncidentFilterParams()
  const [page, setPage] = useState(0) // zero-based for MUI's TablePagination
  const [pageSize, setPageSize] = useState(25)
  const [openIncidentId, setOpenIncidentId] = useState<string | null>(null)

  const filters: IncidentFilters = { ...filterValues, page: page + 1, pageSize }
  const incidentsQuery = useQuery({
    queryKey: operationalFailuresApi.OPERATIONAL_FAILURE_QUERY_KEYS.incidents(filters),
    queryFn: () => operationalFailuresApi.getIncidents(filters),
    placeholderData: keepPreviousData,
  })

  return (
    <AdminShell title="Operational failures" subtitle="What failed for users, why, and where to fix it">
      <Paper elevation={1} sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', p: 2 }}>
        <IncidentFilterBar
          params={params}
          onChange={(patch: Partial<IncidentFilterParams>) => {
            update(patch)
            setPage(0)
          }}
        />
        <IncidentTable
          incidents={incidentsQuery.data?.items ?? []}
          totalCount={incidentsQuery.data?.totalCount ?? 0}
          page={page}
          pageSize={pageSize}
          isLoading={incidentsQuery.isLoading}
          errorMessage={incidentsQuery.isError ? errorMessage(incidentsQuery.error) : null}
          onRetry={() => void incidentsQuery.refetch()}
          onPageChange={setPage}
          onPageSizeChange={(next) => {
            setPageSize(next)
            setPage(0)
          }}
          onOpen={setOpenIncidentId}
        />
      </Paper>
      <IncidentDrawer incidentId={openIncidentId} onClose={() => setOpenIncidentId(null)} onOpenIncident={setOpenIncidentId} />
    </AdminShell>
  )
}
