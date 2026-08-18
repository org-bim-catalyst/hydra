import { expect, test } from '@playwright/test'

/**
 * User Story 5 — running an agent to completion, then independently reopening its history shows
 * every recorded field matching what happened (specs/020-ai-agent-framework quickstart.md
 * Scenario 5; spec.md FR-036/FR-050).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent E2E suite so it runs unmodified
 * once a real environment is wired into CI.
 */

test.describe('Agent execution history', () => {
  test('a completed execution appears in history and its detail page matches what ran', async ({ page }) => {
    await page.goto('/agents')
    await page.getByRole('button', { name: 'New Agent' }).click()
    await page.getByLabel('Name').fill('History Agent')
    await page.getByLabel('System Instructions').fill('You are a helpful assistant.')
    await page.getByLabel('AI Provider').selectOption({ index: 1 })
    await page.getByLabel('Model').selectOption({ index: 1 })
    await page.getByRole('button', { name: 'Create Agent' }).click()
    await page.getByRole('button', { name: 'Publish' }).click()

    await page.getByLabel('Objective').fill('Say hello in one sentence.')
    await page.getByRole('button', { name: 'Run' }).click()
    await expect(page.getByText('Completed')).toBeVisible({ timeout: 30_000 })
    const resultText = await page.locator('[data-testid="execution-result"]').textContent()

    // Independently reopen it from the history list rather than following the just-run state.
    await page.reload()
    await expect(page.locator('[data-testid="execution-history-row"]').first()).toBeVisible()
    await page.locator('[data-testid="execution-history-row"]').first().click()

    await expect(page).toHaveURL(/\/agents\/[0-9a-f-]+\/executions\/[0-9a-f-]+/)
    await expect(page.getByText('Completed')).toBeVisible()
    await expect(page.getByText(resultText ?? '')).toBeVisible()
  })
})
