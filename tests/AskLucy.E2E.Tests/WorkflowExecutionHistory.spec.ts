import { expect, test } from '@playwright/test'

/**
 * User Story 8 — full inspectable history for any past execution (per-node results, errors,
 * approvals, usage, cost), a monitoring/statistics view across a user's workflows, and strict
 * cross-user access denial (specs/022-workflow-orchestration-engine quickstart.md; spec.md
 * FR-050/FR-051/FR-059). Runs a workflow to completion, opens its execution history from the
 * designer's overflow menu, and confirms the Workflow Library's statistics dashboard reflects it.
 *
 * Draft authoring goes through the API directly, same convention as `WorkflowFailureRecovery.spec.ts`.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent/Workflow E2E suite so it runs
 * unmodified once a real environment is wired into CI.
 */

async function createPublishedWorkflow(page: import('@playwright/test').Page, name: string, draftDefinitionJson: string) {
  await page.goto('/workflows')
  await page.getByRole('button', { name: 'New Workflow' }).click()
  await page.getByLabel('Name').fill(name)
  await page.getByRole('button', { name: 'Create Workflow' }).click()
  await expect(page).toHaveURL(/\/workflows\/([0-9a-f-]+)/)
  const workflowId = page.url().match(/\/workflows\/([0-9a-f-]+)/)?.[1] as string

  const current = await (await page.request.get(`/api/v1/workflows/${workflowId}`)).json()
  await page.request.put(`/api/v1/workflows/${workflowId}`, {
    data: { name: current.name, description: current.description, draftDefinitionJson },
  })
  const publishResponse = await page.request.post(`/api/v1/workflows/${workflowId}/versions`, { data: { changeDescription: 'v1' } })
  expect(publishResponse.ok()).toBe(true)

  return workflowId
}

const linearDraftJson = JSON.stringify({
  errorPolicyJson: '{"strategy":"Stop"}',
  nodes: [
    { nodeKey: 'start', nodeType: 'Start', name: 'Start', configurationJson: '{}' },
    { nodeKey: 'end', nodeType: 'End', name: 'End', configurationJson: '{}' },
  ],
  connections: [{ sourceNodeKey: 'start', targetNodeKey: 'end' }],
  variables: [],
})

async function startExecutionAndAwaitCompletion(page: import('@playwright/test').Page, workflowId: string) {
  const started = await (await page.request.post('/api/v1/workflow-executions', { data: { workflowId, inputsJson: '{}' } })).json()

  await expect
    .poll(async () => (await (await page.request.get(`/api/v1/workflow-executions/${started.id}`)).json()).status, { timeout: 30_000 })
    .toBe('Completed')

  return started.id as string
}

test('a completed execution appears in the workflow\'s execution history with a correct status', async ({ page }) => {
  const workflowId = await createPublishedWorkflow(page, 'History Workflow', linearDraftJson)
  const executionId = await startExecutionAndAwaitCompletion(page, workflowId)

  await page.goto(`/workflows/${workflowId}`)
  await page.getByRole('button', { name: 'More workflow actions' }).click()
  await page.getByRole('menuitem', { name: 'Execution History' }).click()

  const historyList = page.getByTestId('execution-history-list')
  await expect(historyList).toBeVisible()
  const row = page.getByTestId('execution-history-row').first()
  await expect(row).toContainText('Completed')

  await row.click()
  await expect(page).toHaveURL(new RegExp(`/workflows/${workflowId}/executions/${executionId}`))
})

test('the Workflow Library statistics dashboard reflects at least one completed execution', async ({ page }) => {
  const workflowId = await createPublishedWorkflow(page, 'Stats Workflow', linearDraftJson)
  await startExecutionAndAwaitCompletion(page, workflowId)

  await page.goto('/workflows')

  const dashboard = page.getByTestId('workflow-statistics-dashboard')
  await expect(dashboard).toBeVisible()
  await expect(dashboard).toContainText('Completed')
})

test('a per-node result set is available for a completed execution via the API', async ({ page }) => {
  const workflowId = await createPublishedWorkflow(page, 'Node Results Workflow', linearDraftJson)
  const executionId = await startExecutionAndAwaitCompletion(page, workflowId)

  const nodesResponse = await page.request.get(`/api/v1/workflow-executions/${executionId}/nodes`)
  expect(nodesResponse.ok()).toBe(true)
  const nodes = await nodesResponse.json()
  expect(nodes.length).toBeGreaterThanOrEqual(2) // Start + End at minimum
  expect(nodes.every((n: { status: string }) => n.status === 'Completed')).toBe(true)

  const usageResponse = await page.request.get(`/api/v1/workflow-executions/${executionId}/usage`)
  expect(usageResponse.ok()).toBe(true)
})
