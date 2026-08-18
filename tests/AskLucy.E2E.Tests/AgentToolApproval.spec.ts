import { expect, test } from '@playwright/test'

/**
 * User Story 3 — a High-risk tool call pauses execution for explicit approval, shows the intended
 * action/parameters before a decision is made, and the decision determines whether the execution
 * completes or ends with an explanation (specs/020-ai-agent-framework quickstart.md Scenario 3;
 * spec.md FR-025/FR-027/FR-028).
 *
 * `FakeHighRiskTool` is registered only in Development/Testing (never Production, see
 * `DependencyInjection.AddApplication`) — exactly the environment this suite is meant to run in.
 * The Agent Builder UI has no tool-configuration surface yet (see AgentMultiStepToolExecution.spec.ts's
 * file header for why); tool attachment goes through the API, matching that same convention.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent E2E suite so it runs unmodified
 * once a real environment is wired into CI.
 */

async function createAgentWithHighRiskTool(page: import('@playwright/test').Page, name: string) {
  await page.goto('/agents')
  await page.getByRole('button', { name: 'New Agent' }).click()
  await page.getByLabel('Name').fill(name)
  await page.getByLabel('System Instructions').fill('You are a helpful assistant with access to a risky action.')
  await page.getByLabel('AI Provider').selectOption({ index: 1 })
  await page.getByLabel('Model').selectOption({ index: 1 })
  await page.getByRole('button', { name: 'Create Agent' }).click()

  await expect(page).toHaveURL(/\/agents\/([0-9a-f-]+)/)
  const agentId = page.url().match(/\/agents\/([0-9a-f-]+)/)?.[1]

  const current = await (await page.request.get(`/api/v1/agents/${agentId}`)).json()
  await page.request.put(`/api/v1/agents/${agentId}`, {
    data: {
      name: current.name,
      description: current.description,
      agentType: current.agentType,
      instructions: current.instructions,
      modelProviderId: current.modelProviderId,
      modelId: current.modelId,
      outputFormat: current.outputFormat,
      executionPolicy: current.executionPolicy,
      tools: [{ toolName: 'FakeHighRiskTool', configurationJson: null }],
    },
  })

  await page.reload()
  await page.getByRole('button', { name: 'Publish' }).click()

  return agentId
}

test.describe('Agent tool approval', () => {
  test('approving the pending approval lets the execution complete', async ({ page }) => {
    await createAgentWithHighRiskTool(page, 'Risky Agent — Approve')

    await page.getByLabel('Objective').fill('Perform the risky action.')
    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.getByText('WaitingForApproval')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('heading', { name: 'This agent wants to take an action' })).toBeVisible()
    await expect(page.locator('[data-testid="approval-parameters"]')).toBeVisible()

    await page.getByRole('button', { name: 'Approve' }).click()

    await expect(page.getByText('Completed')).toBeVisible({ timeout: 30_000 })
  })

  test('rejecting the pending approval ends the execution with an explanation, never running the tool', async ({ page }) => {
    await createAgentWithHighRiskTool(page, 'Risky Agent — Reject')

    await page.getByLabel('Objective').fill('Perform the risky action.')
    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.getByText('WaitingForApproval')).toBeVisible({ timeout: 30_000 })

    await page.getByLabel('Reason (optional, shown if you reject)').fill('Not authorized for this run.')
    await page.getByRole('button', { name: 'Reject' }).click()

    await expect(page.getByText('Failed')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(/Not authorized for this run\./)).toBeVisible()
  })
})
