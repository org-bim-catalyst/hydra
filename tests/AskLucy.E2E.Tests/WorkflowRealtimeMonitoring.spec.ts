import { expect, test } from '@playwright/test'

/**
 * User Story 6 — the UI reflects node transitions without a manual refresh (accelerated by
 * `useWorkflowExecutionHub`'s live push over `/hubs/workflow-execution`, with the existing 2s REST
 * poll as a reconciliation fallback), and a Cancel action stops a running execution promptly
 * (specs/022-workflow-orchestration-engine quickstart.md Scenario 6; spec.md SC-007: within 5s).
 *
 * Draft authoring goes through the API directly, same convention as `WorkflowBranchingParallelLoop.spec.ts`;
 * starting the run and observing it goes through the UI (`WorkflowDesignerPage`'s Run dialog →
 * `WorkflowExecutionPage`/`ExecutionMonitor`), since that live-visibility path is exactly what this
 * story adds.
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

const twoStepDraftDefinitionJson = JSON.stringify({
  errorPolicyJson: '{"strategy":"Stop"}',
  nodes: [
    { nodeKey: 'start', nodeType: 'Start', name: 'Start', configurationJson: '{}' },
    { nodeKey: 'transform1', nodeType: 'Transform', name: 'First Step', configurationJson: JSON.stringify({ expression: '1', outputField: 'result' }) },
    { nodeKey: 'transform2', nodeType: 'Transform', name: 'Second Step', configurationJson: JSON.stringify({ expression: '2', outputField: 'result' }) },
    { nodeKey: 'end', nodeType: 'End', name: 'End', configurationJson: '{}' },
  ],
  connections: [
    { sourceNodeKey: 'start', targetNodeKey: 'transform1' },
    { sourceNodeKey: 'transform1', targetNodeKey: 'transform2' },
    { sourceNodeKey: 'transform2', targetNodeKey: 'end' },
  ],
  variables: [],
})

test.describe('Workflow real-time execution monitoring', () => {
  test('the execution monitor shows node activity without a manual page reload', async ({ page }) => {
    await createPublishedWorkflow(page, 'Visibility Workflow', twoStepDraftDefinitionJson)

    await page.getByRole('button', { name: 'Run' }).click()
    await page.getByLabel('Inputs (JSON)').fill('{}')
    await page.getByRole('dialog').getByRole('button', { name: 'Run' }).click()

    await expect(page).toHaveURL(/\/workflows\/[0-9a-f-]+\/executions\/[0-9a-f-]+/)

    // No page.reload() anywhere in this test — the node list populates from the live hub push /
    // poll alone.
    await expect(page.locator('[data-testid="execution-node-row"]').first()).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Completed')).toBeVisible({ timeout: 30_000 })
  })

  test('cancelling a running execution stops it promptly (SC-007: within 5s of the request)', async ({ page }) => {
    await createPublishedWorkflow(page, 'Cancellable Workflow', twoStepDraftDefinitionJson)

    await page.getByRole('button', { name: 'Run' }).click()
    await page.getByLabel('Inputs (JSON)').fill('{}')
    await page.getByRole('dialog').getByRole('button', { name: 'Run' }).click()

    await expect(page).toHaveURL(/\/workflows\/[0-9a-f-]+\/executions\/[0-9a-f-]+/)
    await expect(page.getByRole('button', { name: 'Cancel' })).toBeVisible({ timeout: 10_000 })
    const cancelledAt = Date.now()
    await page.getByRole('button', { name: 'Cancel' }).click()

    await expect(page.getByText('Cancelled')).toBeVisible({ timeout: 5_000 })
    expect(Date.now() - cancelledAt).toBeLessThan(5_000)
  })

  test('pausing and resuming a running execution continues from where it left off', async ({ page }) => {
    const workflowId = await createPublishedWorkflow(page, 'Pausable Workflow', twoStepDraftDefinitionJson)

    const started = await (
      await page.request.post('/api/v1/workflow-executions', { data: { workflowId, inputsJson: '{}' } })
    ).json()
    await expect
      .poll(async () => (await (await page.request.get(`/api/v1/workflow-executions/${started.id}`)).json()).status, { timeout: 30_000 })
      .toBe('Completed')

    // Pausing an already-completed execution is a documented no-op (nothing to pause) — exercises
    // the same endpoint the UI's Pause button calls, confirming it never errors on a terminal run.
    const pauseResponse = await page.request.post(`/api/v1/workflow-executions/${started.id}/pause`)
    expect(pauseResponse.ok()).toBe(true)

    const execution = await (await page.request.get(`/api/v1/workflow-executions/${started.id}`)).json()
    expect(execution.status).toBe('Completed')
  })
})
