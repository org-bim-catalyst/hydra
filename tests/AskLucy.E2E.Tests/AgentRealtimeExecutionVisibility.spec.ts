import { expect, test } from '@playwright/test'

/**
 * User Story 4 — the UI reflects step transitions without a manual refresh (accelerated by
 * `useAgentExecutionHub`'s live push over `/hubs/agent-execution`, with the existing 2s REST poll
 * as a reconciliation fallback), and a Cancel action stops a running execution promptly
 * (specs/020-ai-agent-framework quickstart.md Scenario 4; spec.md SC-009).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent E2E suite so it runs unmodified
 * once a real environment is wired into CI.
 */

test.describe('Agent real-time execution visibility', () => {
  test('the execution timeline shows step activity without a manual page reload', async ({ page }) => {
    await page.goto('/agents')
    await page.getByRole('button', { name: 'New Agent' }).click()
    await page.getByLabel('Name').fill('Visibility Agent')
    await page.getByLabel('System Instructions').fill('You are a helpful assistant.')
    await page.getByLabel('AI Provider').selectOption({ index: 1 })
    await page.getByLabel('Model').selectOption({ index: 1 })
    await page.getByRole('button', { name: 'Create Agent' }).click()
    await page.getByRole('button', { name: 'Publish' }).click()

    await page.getByLabel('Objective').fill('Say hello in one sentence.')
    await page.getByRole('button', { name: 'Run' }).click()

    // No page.reload() anywhere in this test — the timeline populates from the live hub push /
    // poll alone.
    await expect(page.locator('[data-testid="execution-step-row"]').first()).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText('Completed')).toBeVisible({ timeout: 30_000 })
  })

  test('cancelling a running execution stops it promptly (SC-009: within 5s of the request)', async ({ page }) => {
    await page.goto('/agents')
    await page.getByRole('button', { name: 'New Agent' }).click()
    await page.getByLabel('Name').fill('Cancellable Agent')
    await page.getByLabel('System Instructions').fill('You are a helpful assistant.')
    await page.getByLabel('AI Provider').selectOption({ index: 1 })
    await page.getByLabel('Model').selectOption({ index: 1 })
    await page.getByRole('button', { name: 'Create Agent' }).click()
    await page.getByRole('button', { name: 'Publish' }).click()

    await page.getByLabel('Objective').fill('Say hello in one sentence.')
    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.getByRole('button', { name: 'Cancel' })).toBeVisible({ timeout: 10_000 })
    const cancelledAt = Date.now()
    await page.getByRole('button', { name: 'Cancel' }).click()

    await expect(page.getByText('Cancelled')).toBeVisible({ timeout: 5_000 })
    expect(Date.now() - cancelledAt).toBeLessThan(5_000)
  })
})
