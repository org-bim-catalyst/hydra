import { Fragment, useState } from 'react'
import {
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
} from '@mui/material'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import ExpandLessIcon from '@mui/icons-material/ExpandLess'
import { visuallyHidden } from '@mui/utils'
import { useQuery } from '@tanstack/react-query'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import { AdminShell } from '../components/AdminShell'
import { AiProviderActionsMenu } from '../components/AiProviderActionsMenu'
import { ProviderHealthCell } from '../components/ProviderHealthCell'
import { ProviderStalenessCell } from '../components/ProviderStalenessCell'
import { ProviderModelsSection } from '../components/ProviderModelsSection'

const ADMIN_AI_PROVIDERS_QUERY_KEY = ['admin', 'ai-providers']

/**
 * Admin AI provider configuration (specs/007-admin-ai-provider-ui) — the missing
 * administrator-facing surface for the already-shipped AdminAiProvidersController
 * (specs/005-multi-provider-ai-engine). Mirrors AdminUsersPage.tsx's table shape.
 */
export function AdminAiProvidersPage() {
  const { data: providers, isLoading } = useQuery({
    queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY,
    queryFn: adminAiProvidersApi.getProviders,
  })
  const [expandedProviderId, setExpandedProviderId] = useState<string | null>(null)

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
      <Paper elevation={1} sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TableContainer ref={tableRef} sx={{ flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight }}>
          <Table sx={{ height: showsStatusRow ? '100%' : undefined }}>
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
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {isLoading && <TableLoadingRow colSpan={7} />}
              {!isLoading && (providers ?? []).length === 0 && (
                <TableEmptyRow colSpan={7} message="No AI providers found." />
              )}
              {providers?.map((provider) => {
                const isExpanded = expandedProviderId === provider.id
                return (
                  <Fragment key={provider.id}>
                    <TableRow hover>
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
                      <TableCell align="right">
                        <AiProviderActionsMenu provider={provider} />
                      </TableCell>
                    </TableRow>
                    <TableRow>
                      <TableCell
                        colSpan={7}
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
    </AdminShell>
  )
}
