import { Alert, CircularProgress } from '@mui/material'
import type { DocumentDashboardSummary } from '../api/documentsApi'
import { DashboardBody } from './ProcessingDashboard'

interface OrganizationDashboardProps {
  data?: DocumentDashboardSummary
  isLoading: boolean
  isError: boolean
}

/** FR-045a, US6 AC6 — same shape as the per-user dashboard, aggregated organization-wide. Role-gating happens where this is rendered (the existing `useIsAdmin` hook), not inside this component. */
export function OrganizationDashboard({ data, isLoading, isError }: OrganizationDashboardProps) {
  if (isError) {
    return <Alert severity="error">Could not load the organization dashboard. Please try again.</Alert>
  }

  if (isLoading || !data) {
    return <CircularProgress size={24} aria-label="Loading the organization-wide dashboard" />
  }

  return <DashboardBody data={data} />
}
