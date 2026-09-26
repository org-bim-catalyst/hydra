import { Fragment, useCallback, useRef, useState } from 'react'
import {
  Alert,
  Box,
  Chip,
  Collapse,
  IconButton,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import ExpandLessIcon from '@mui/icons-material/ExpandLess'
import { visuallyHidden } from '@mui/utils'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { useIsAdmin } from '../../../hooks/useIsAdmin'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import { useCan } from '../../auth/hooks/usePermissions'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import { AdminShell } from '../components/AdminShell'
import { AiProviderActionsMenu } from '../components/AiProviderActionsMenu'
import { ProviderHealthCell } from '../components/ProviderHealthCell'
import { ProviderStalenessCell } from '../components/ProviderStalenessCell'
import { ProviderModelsSection } from '../components/ProviderModelsSection'
import { CustomModelsSection } from '../components/customModels/CustomModelsSection'

const ADMIN_AI_PROVIDERS_QUERY_KEY = ['admin', 'ai-providers']

/**
 * Admin AI provider configuration (specs/007-admin-ai-provider-ui) — the missing
 * administrator-facing surface for the already-shipped AdminAiProvidersController
 * (specs/005-multi-provider-ai-engine). Mirrors AdminUsersPage.tsx's table shape.
 */
export function AdminAiProvidersPage() {
  // The page is reachable with any of several view permissions (adminNav.tsx), so each part asks
  // for its own rather than letting a caller without it hit a 403 (specs/072 research D11).
  const isAdmin = useIsAdmin()
  const canViewProviders = useCan('admin.ai-providers.view') || isAdmin
  const canViewCustomModels = useCan('admin.custom-models.view') || isAdmin

  const { data: providers, isLoading } = useQuery({
    queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY,
    queryFn: adminAiProvidersApi.getProviders,
    enabled: canViewProviders,
  })
  const [expandedProviderId, setExpandedProviderId] = useState<string | null>(null)

  // `?select=<providerId>` — the corrective-action link from an operational failure incident
  // (specs/074 FR-017) lands on the provider it names: highlighted and scrolled into view once.
  const [searchParams] = useSearchParams()
  const [selectedProviderId] = useState(() => searchParams.get('select'))
  const hasScrolledToSelection = useRef(false)
  const selectedRowRef = useCallback((row: HTMLTableRowElement | null) => {
    if (!row || hasScrolledToSelection.current) return
    hasScrolledToSelection.current = true
    row.scrollIntoView?.({ block: 'center' })
  }, [])
  const selectionIsMissing =
    selectedProviderId !== null &&
    providers !== undefined &&
    !providers.some((provider) => provider.id === selectedProviderId)

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && (providers ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <AdminShell
      title="AI providers"
      subtitle="Enable a provider, configure its credential, and mark which of its models are available"

    >
      {selectionIsMissing && (
        <Alert severity="info" sx={{ mb: 2 }}>
          The provider this link pointed to no longer exists.
        </Alert>
      )}
      {canViewProviders && (
        // The two sections share the page height equally (flex: 1 each); each scrolls on its own.
        <Paper
          elevation={1}
          component="section"
          aria-labelledby="frontier-models-heading"
          sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}
        >
          <Typography id="frontier-models-heading" variant="subtitle1" component="h2" sx={{ p: 2 }}>
            Frontier models
          </Typography>
          <TableContainer ref={tableRef} sx={{ flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight }}>
            <Table stickyHeader aria-labelledby="frontier-models-heading" sx={{ height: showsStatusRow ? '100%' : undefined }}>
              <TableHead>
                <TableRow>
                  <TableCell>
                    <Box component="span" sx={visuallyHidden}>
                      Expand
                    </Box>
                  </TableCell>
                  <TableCell>Provider</TableCell>
                  <TableCell>Enabled</TableCell>
                  <TableCell>Credential</TableCell>
                  <TableCell>Health</TableCell>
                  <TableCell>Last confirmed</TableCell>
                  <TableCell>Credential hint</TableCell>
                  <TableCell align="right">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {isLoading && <TableLoadingRow colSpan={8} />}
                {!isLoading && (providers ?? []).length === 0 && (
                  <TableEmptyRow colSpan={8} message="No AI providers found." />
                )}
                {providers?.map((provider) => {
                  const isExpanded = expandedProviderId === provider.id
                  const isSelected = selectedProviderId === provider.id
                  return (
                    <Fragment key={provider.id}>
                      <TableRow hover selected={isSelected} ref={isSelected ? selectedRowRef : undefined}>
                        <TableCell>
                          <IconButton
                            size="small"
                            aria-label={
                              isExpanded
                                ? `Collapse models for ${provider.displayName}`
                                : `Expand models for ${provider.displayName}`
                            }
                            onClick={() => setExpandedProviderId(isExpanded ? null : provider.id)}
                          >
                            {isExpanded ? (
                              <ExpandLessIcon fontSize="small" />
                            ) : (
                              <ExpandMoreIcon fontSize="small" />
                            )}
                          </IconButton>
                        </TableCell>
                        <TableCell>{provider.displayName}</TableCell>
                        <TableCell>
                          <Chip
                            size="small"
                            label={provider.isEnabled ? 'Enabled' : 'Disabled'}
                            color={provider.isEnabled ? 'success' : 'default'}
                            variant="outlined"
                          />
                        </TableCell>
                        <TableCell>
                          <Chip
                            size="small"
                            label={provider.hasCredential ? 'Configured' : 'Not configured'}
                            color={provider.hasCredential ? 'success' : 'default'}
                            variant="outlined"
                          />
                        </TableCell>
                        <TableCell>
                          <ProviderHealthCell provider={provider} />
                        </TableCell>
                        <TableCell>
                          <ProviderStalenessCell provider={provider} />
                        </TableCell>
                        <TableCell>
                          <Typography variant="body2" color={provider.credentialHint ? 'text.primary' : 'text.secondary'}>
                            {provider.credentialHint ?? 'Not set'}
                          </Typography>
                        </TableCell>
                        <TableCell align="right">
                          <AiProviderActionsMenu provider={provider} />
                        </TableCell>
                      </TableRow>
                      <TableRow>
                        <TableCell
                          colSpan={8}
                          sx={{ p: 0, borderBottom: isExpanded ? undefined : 'none' }}
                        >
                          <Collapse in={isExpanded} unmountOnExit>
                            <ProviderModelsSection provider={provider} />
                          </Collapse>
                        </TableCell>
                      </TableRow>
                    </Fragment>
                  )
                })}
              </TableBody>
            </Table>
          </TableContainer>
        </Paper>
      )}
      {canViewCustomModels && <CustomModelsSection />}
    </AdminShell>
  )
}
