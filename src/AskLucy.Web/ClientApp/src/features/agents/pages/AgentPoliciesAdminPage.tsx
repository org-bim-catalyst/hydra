import { AppShell } from '../../../components/AppShell'
import { AgentPolicyAdminPanel } from '../components/AgentPolicyAdminPanel'

/** spec.md User Story 3 — Administrator/Super User-only agent auto-approval policy management. */
export function AgentPoliciesAdminPage() {
  return (
    <AppShell
      title="Agent policies"
      subtitle="Pre-approve specific high-risk agent actions so they run without an interactive approval prompt"
    >
      <AgentPolicyAdminPanel />
    </AppShell>
  )
}
