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
  Typography,
} from '@mui/material'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import ExpandLessIcon from '@mui/icons-material/ExpandLess'
import SyncIcon from '@mui/icons-material/Sync'
import { visuallyHidden } from '@mui/utils'
import { useQuery } from '@tanstack/react-query'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import { PageHeader } from '../../../components/PageHeader'
import { AiProviderActionsMenu } from '../components/AiProviderActionsMenu'
import { AiModelStatusMenu } from '../components/AiModelStatusMenu'
import { ModelSyncDialog } from '../components/ModelSyncDialog'

const HEALTH_COLOR: Record<AdminAiProvider['healthStatus'], 'success' | 'error' | 'default'> = {
  Healthy: 'success',
  Unhealthy: 'error',
  Unknown: 'default',
}

const MODEL_STATUS_COLOR: Record<AdminAiModel['status'], 'success' | 'warning' | 'default'> = {
  Available: 'success',
  Deprecated: 'warning',
  Unavailable: 'default',
}

const ADMIN_AI_PROVIDERS_QUERY_KEY = ['admin', 'ai-providers']

function formatPricing(pricing: AdminAiModel['pricing']) {
  if (!pricing) return 'Unknown'
  return `$${pricing.inputPerMillionTokensUsd}/$${pricing.outputPerMillionTokensUsd} per 1M tokens (in/out)`
}

interface ProviderModelsSectionProps {
  provider: AdminAiProvider
}

/** specs/008-ai-model-catalog-management US1-US3 — the expanded content for one provider row. */
function ProviderModelsSection({ provider }: ProviderModelsSectionProps) {
  const [syncDialogOpen, setSyncDialogOpen] = useState(false)
  const { data: models } = useQuery({
    queryKey: ['admin', 'ai-providers', provider.id, 'models'],
    queryFn: () => adminAiProvidersApi.getModels(provider.id),
  })

  return (
    <Box sx={{ p: 2, bgcolor: 'action.hover' }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="subtitle1">Models</Typography>
        <Button size="small" startIcon={<SyncIcon fontSize="small" />} onClick={() => setSyncDialogOpen(true)}>
          Sync from provider
        </Button>
      </Box>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Model</TableCell>
            <TableCell>Capabilities</TableCell>
            <TableCell>Pricing</TableCell>
            <TableCell>Status</TableCell>
            <TableCell align="right">Actions</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {models?.map((model) => (
            <TableRow key={model.id}>
              <TableCell>
                <Typography variant="body2">{model.displayName}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {model.modelKey}
                </Typography>
              </TableCell>
              <TableCell>
                {Object.entries(model.capabilities)
                  .filter(([, supported]) => supported)
                  .map(([capability]) => (
                    <Chip key={capability} size="small" label={capability} sx={{ mr: 0.5, mb: 0.5 }} />
                  ))}
              </TableCell>
              <TableCell>{formatPricing(model.pricing)}</TableCell>
              <TableCell>
                <Chip size="small" label={model.status} color={MODEL_STATUS_COLOR[model.status]} variant="outlined" />
              </TableCell>
              <TableCell align="right">
                <AiModelStatusMenu model={model} providerId={provider.id} />
              </TableCell>
            </TableRow>
          ))}
          {models?.length === 0 && (
            <TableRow>
              <TableCell colSpan={5}>
                <Typography variant="body2" color="text.secondary">
                  No models in the catalog yet — try syncing from the provider.
                </Typography>
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
      <ModelSyncDialog
        providerId={provider.id}
        providerDisplayName={provider.displayName}
        open={syncDialogOpen}
        onClose={() => setSyncDialogOpen(false)}
      />
    </Box>
  )
}

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
    <Box sx={{ p: { xs: 2, sm: 4 }, bgcolor: 'background.default', minHeight: '100%' }}>
      <PageHeader
        backTo="/admin/dashboard"
        backLabel="Back to dashboard"
        title="AI providers"
        subtitle="Enable a provider and configure its credential before end users can select it"
      />
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
                          aria-label={isExpanded ? `Collapse models for ${provider.displayName}` : `Expand models for ${provider.displayName}`}
                          onClick={() => setExpandedProviderId(isExpanded ? null : provider.id)}
                        >
                          {isExpanded ? <ExpandLessIcon fontSize="small" /> : <ExpandMoreIcon fontSize="small" />}
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
                        <Chip size="small" label={provider.healthStatus} color={HEALTH_COLOR[provider.healthStatus]} variant="outlined" />
                        {provider.healthStatusCheckedAtUtc && (
                          <Box component="span" sx={{ ml: 1, fontSize: '0.75rem', color: 'text.secondary' }}>
                            {new Date(provider.healthStatusCheckedAtUtc).toLocaleString()}
                          </Box>
                        )}
                      </TableCell>
                      <TableCell align="right">
                        <AiProviderActionsMenu provider={provider} />
                      </TableCell>
                    </TableRow>
                    <TableRow>
                      <TableCell colSpan={6} sx={{ p: 0, borderBottom: isExpanded ? undefined : 'none' }}>
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
    </Box>
  )
}
