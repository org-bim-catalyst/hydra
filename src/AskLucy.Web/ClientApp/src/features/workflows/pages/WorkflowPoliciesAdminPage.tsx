import { AdminShell } from '../../admin/components/AdminShell'
import { WorkflowPolicyAdminPanel } from '../components/WorkflowPolicyAdminPanel'

/** spec.md User Story 5 — Administrator/Super User-only workflow auto-approval policy management. */
export function WorkflowPoliciesAdminPage() {
  return (
    <AdminShell
      title="Workflow policies"
      subtitle="Pre-approve specific high-risk workflow steps so they run without an interactive approval prompt"
    >
      <WorkflowPolicyAdminPanel />
    </AdminShell>
  )
}
