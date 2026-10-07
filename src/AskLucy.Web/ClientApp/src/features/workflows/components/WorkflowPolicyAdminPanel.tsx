import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  IconButton,
  Paper,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
} from '@mui/material'
import DeleteIcon from '@mui/icons-material/Delete'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useT } from '../../../i18n/useT'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import { AdminSectionActions } from '../../admin/components/AdminSectionActions'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import * as workflowPoliciesApi from '../api/workflowPoliciesApi'
import type { SaveWorkflowPolicyInput, WorkflowPolicy } from '../api/workflowPoliciesApi'
import { WorkflowPolicyFormDialog } from './WorkflowPolicyFormDialog'
import { NODE_TYPES } from './workflowPolicyNodeTypes'

const WORKFLOW_POLICIES_QUERY_KEY = ['admin', 'workflow-policies']

/**
 * Administrator-managed auto-approval rule CRUD for the workflow engine's platform-mandatory
 * approval baseline (spec.md "Approval Policies") — lets a Human Approval node or a High/Critical-risk
 * capability node proceed without an interactive approval prompt when it matches an enabled policy.
 * Only reachable by Administrator/Super User (the API itself enforces this regardless of what
 * renders this component). Unlike the Agent Runtime's single `toolName` targeting, a policy here
 * targets a node type and/or an underlying tool name (mirrors `WorkflowPolicy.Create`'s domain rule
 * that at least one of the two must be set).
 */
export function WorkflowPolicyAdminPanel() {
  const t = useT('admin.workflowPolicies')
  const queryClient = useQueryClient()
  const { data: policies, isLoading } = useQuery({ queryKey: WORKFLOW_POLICIES_QUERY_KEY, queryFn: workflowPoliciesApi.listWorkflowPolicies })

  const [isFormOpen, setIsFormOpen] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  // A node type the catalog does not know (a newer server) is shown as returned.
  const nodeTypeLabel = (nodeType: string) =>
    (NODE_TYPES as readonly string[]).includes(nodeType)
      ? t(`nodeTypes.${nodeType as (typeof NODE_TYPES)[number]}`)
      : nodeType

  const invalidate = () => queryClient.invalidateQueries({ queryKey: WORKFLOW_POLICIES_QUERY_KEY })

  const openForm = () => {
    setErrorMessage(null)
    setIsFormOpen(true)
  }

  const createPolicy = useMutation({
    mutationFn: (input: SaveWorkflowPolicyInput) => workflowPoliciesApi.createWorkflowPolicy(input),
    onSuccess: () => {
      setIsFormOpen(false)
      invalidate()
    },
    onError: (err) => setErrorMessage(err instanceof Error ? err.message : t('panel.errors.create')),
  })

  const toggleEnabled = useMutation({
    mutationFn: (policy: WorkflowPolicy) =>
      workflowPoliciesApi.updateWorkflowPolicy(policy.id, {
        name: policy.name,
        description: policy.description,
        conditionsJson: policy.conditionsJson,
        isEnabled: !policy.isEnabled,
      }),
    onSuccess: invalidate,
    onError: (err) => setErrorMessage(err instanceof Error ? err.message : t('panel.errors.update')),
  })

  const deletePolicy = useMutation({
    mutationFn: (id: string) => workflowPoliciesApi.deleteWorkflowPolicy(id),
    onSuccess: invalidate,
    onError: (err) => setErrorMessage(err instanceof Error ? err.message : t('panel.errors.delete')),
  })

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && (policies ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
      <AdminSectionActions>
        <Button variant="contained" onClick={openForm}>
          {t('panel.newPolicy')}
        </Button>
      </AdminSectionActions>

      {errorMessage && (
        <Alert severity="error" sx={{ mb: 2 }}>
          {errorMessage}
        </Alert>
      )}

      <Paper sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TableContainer ref={tableRef} sx={{ flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight }}>
          <Table sx={{ '& tr:last-child td': { border: 0 }, height: showsStatusRow ? '100%' : undefined }}>
            <TableHead>
              <TableRow>
                <TableCell>{t('panel.columns.name')}</TableCell>
                <TableCell>{t('panel.columns.nodeType')}</TableCell>
                <TableCell>{t('panel.columns.underlyingTool')}</TableCell>
                <TableCell>{t('panel.columns.conditions')}</TableCell>
                <TableCell>{t('panel.columns.enabled')}</TableCell>
                <TableCell sx={{ textAlign: 'end' }}>{t('panel.columns.actions')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {isLoading && <TableLoadingRow colSpan={6} />}
              {!isLoading && (policies ?? []).length === 0 && (
                <TableEmptyRow colSpan={6} message={t('panel.empty')} />
              )}
              {(policies ?? []).map((policy) => (
                <TableRow key={policy.id}>
                  <TableCell>{policy.name}</TableCell>
                  <TableCell>{policy.workflowNodeType && <Chip label={nodeTypeLabel(policy.workflowNodeType)} size="small" />}</TableCell>
                  <TableCell>{policy.underlyingToolName && (
                      <Chip label={<bdi dir="ltr">{policy.underlyingToolName}</bdi>} size="small" />
                    )}</TableCell>
                  <TableCell>{policy.conditionsJson ?? t('panel.always')}</TableCell>
                  <TableCell>
                    <Switch
                      checked={policy.isEnabled}
                      slotProps={{ input: { 'aria-label': t('panel.enableAria', { name: policy.name }) } }} onChange={() => toggleEnabled.mutate(policy)} disabled={toggleEnabled.isPending} />
                  </TableCell>
                  <TableCell sx={{ textAlign: 'end' }}>
                    <IconButton aria-label={t('panel.deleteAria', { name: policy.name })} onClick={() => deletePolicy.mutate(policy.id)} disabled={deletePolicy.isPending}>
                      <DeleteIcon fontSize="small" />
                    </IconButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>

      <WorkflowPolicyFormDialog
        open={isFormOpen}
        isSaving={createPolicy.isPending}
        errorMessage={errorMessage}
        onClose={() => setIsFormOpen(false)}
        onSubmit={(input) => createPolicy.mutate(input)}
      />
    </Box>
  )
}
