import { AdminShell } from '../../admin/components/AdminShell'
import { useOuterT } from '../../admin/hooks/useOuterT'
import { AgentPolicyAdminPanel } from '../components/AgentPolicyAdminPanel'

/** spec.md User Story 3 — Administrator/Super User-only agent auto-approval policy management. */
export function AgentPoliciesAdminPage() {
  // The shell takes its title as a prop, so it is resolved here, outside the shell's own language surface.
  const t = useOuterT('admin.agentPolicies')
  return (
    <AdminShell
      title={t('page.title')}
      subtitle={t('page.subtitle')}
    >
      <AgentPolicyAdminPanel />
    </AdminShell>
  )
}
