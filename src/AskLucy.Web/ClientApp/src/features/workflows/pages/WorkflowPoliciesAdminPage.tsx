import { AdminShell } from '../../admin/components/AdminShell'
import { useOuterT } from '../../admin/hooks/useOuterT'
import { WorkflowPolicyAdminPanel } from '../components/WorkflowPolicyAdminPanel'

/** spec.md User Story 5 — Administrator/Super User-only workflow auto-approval policy management. */
export function WorkflowPoliciesAdminPage() {
  // The shell takes its title as a prop, so it is resolved here, outside the shell's own language surface.
  const t = useOuterT('admin.workflowPolicies')
  return (
    <AdminShell
      title={t('page.title')}
      subtitle={t('page.subtitle')}
    >
      <WorkflowPolicyAdminPanel />
    </AdminShell>
  )
}
