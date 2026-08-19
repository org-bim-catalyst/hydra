import { useQuery } from '@tanstack/react-query'
import * as workflowsApi from '../api/workflowsApi'

export const WORKFLOW_VERSIONS_QUERY_KEY = ['workflow-versions']

/** spec.md User Story 3 — every published version of a workflow, newest first. */
export function useWorkflowVersions(workflowId: string | null) {
  return useQuery({
    queryKey: [...WORKFLOW_VERSIONS_QUERY_KEY, workflowId],
    queryFn: () => workflowsApi.listWorkflowVersions(workflowId!),
    enabled: workflowId !== null,
  })
}
