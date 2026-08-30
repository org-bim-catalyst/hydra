import { Fragment, useState } from 'react'
import {
  Box,
  Button,
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
  Tooltip,
} from '@mui/material'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import ExpandLessIcon from '@mui/icons-material/ExpandLess'
import { visuallyHidden } from '@mui/utils'
import { Link as RouterLink } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AppShell } from '../../../components/AppShell'
import { AiProviderActionsMenu } from '../components/AiProviderActionsMenu'
import { ProviderHealthCell } from '../components/ProviderHealthCell'
import { ProviderModelsSection } from '../components/ProviderModelsSection'

const ADMIN_AI_PROVIDERS_QUERY_KEY = ['admin', 'ai-providers']

/**
 * Admin AI provider configuration (specs/007-admin-ai-provider-ui) — the missing
 * administrator-facing surface for the already-shipped AdminAiProvidersController
 * (specs/005-multi-provider-ai-engine). Mirrors AdminUsersPage.tsx's table shape.
 */
export function AdminAiProvidersPage() {
  const { data: providers } = useQuery({
    queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY,
    queryFn: adminAiProvidersApi.getProviders,
  })
  const [expandedProviderId, setExpandedProviderId] = useState<string | null>(null)

  return (
    <AppShell
      title="AI providers"
      subtitle="Enable a provider, configure its credential, and choose the default model it contributes"
      actions={
        <Button component={RouterLink} to="/admin/ai-capabilities" variant="outlined" size="small">
          Manage capabilities
        </Button>
      }
    >
      <Paper elevation={1}>
        <TableContainer>
          <Table>
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
                <TableCell align="right">Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
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
                      <TableCell>
                        {provider.displayName}
                        {/*
                          Visible without expanding the row: this is the provider that serves
                          every request made with no user model preference behind it. It used to
                          be decided silently by alphabetical order, which is why background work
                          ran on a provider the operator had never chosen.
                        */}
                        {provider.isEffectivePlatformDefault && (
                          <Tooltip title="Serves location intent classification, memory extraction and any request with no user model preference">
                            <Chip
                              size="small"
                              label="Platform default"
                              color="primary"
                              variant="outlined"
                              sx={{ ml: 1 }}
                            />
                          </Tooltip>
                        )}
                      </TableCell>
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
                      <TableCell align="right">
                        <AiProviderActionsMenu provider={provider} />
                      </TableCell>
                    </TableRow>
                    <TableRow>
                      <TableCell
                        colSpan={6}
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
    </AppShell>
  )
}
