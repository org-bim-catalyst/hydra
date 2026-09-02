import { useQuery } from '@tanstack/react-query'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AdminShell } from '../components/AdminShell'
import { CapabilityAssignmentsSection } from '../components/CapabilityAssignmentsSection'

const ADMIN_AI_PROVIDERS_QUERY_KEY = ['admin', 'ai-providers']

/**
 * Step three of the three the administrator configures, on its own page: which provider serves
 * each non-chat capability. Steps one and two — enabling a provider with its models, and giving
 * each provider a default model — live on the Providers page, because they are about a provider
 * in isolation. This page is about the platform as a whole, and mixing the two on one screen made
 * it read as a footnote to the provider table rather than the routing decision it is.
 */
export function AdminAiCapabilitiesPage() {
  const { data: providers } = useQuery({
    queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY,
    queryFn: adminAiProvidersApi.getProviders,
  })

  return (
    <AdminShell
      title="AI capabilities"
      subtitle="Choose which provider serves each capability — the model follows from that provider's default"

    >
      <CapabilityAssignmentsSection providers={providers ?? []} />
    </AdminShell>
  )
}
