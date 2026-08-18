import { expect, test } from '@playwright/test'

/**
 * User Story 1 — create an agent with only instructions and a model, run it, and confirm the
 * result is persisted and retrievable afterward (specs/020-ai-agent-framework quickstart.md
 * Scenario 1).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on PromptLifecycle.spec.ts). Written to
 * the same selector/assertion conventions as the existing Prompt Library E2E suite so it runs
 * unmodified once a real environment is wired into CI.
 */

test.describe('Agent create and run', () => {
  test('creating an agent with no tools, running it, and reloading shows the same persisted result', async ({ page }) => {
    await page.goto('/agents')

    await page.getByRole('button', { name: 'New Agent' }).click()
    await page.getByLabel('Name').fill('Concise Assistant')
    await page.getByLabel('System Instructions').fill('You are a concise assistant.')
    await page.getByLabel('AI Provider').selectOption({ index: 1 })
    await page.getByLabel('Model').selectOption({ index: 1 })
    await page.getByRole('button', { name: 'Create Agent' }).click()

    await expect(page).toHaveURL(/\/agents\/[0-9a-f-]+/)
    await page.getByRole('button', { name: 'Publish' }).click()

    await page.getByLabel('Objective').fill('Say hello in one sentence.')
    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.getByText('Completed')).toBeVisible({ timeout: 30_000 })
    const resultText = await page.locator('[data-testid="execution-result"]').textContent()
    expect(resultText).toBeTruthy()

    await page.reload()
    await expect(page.locator('[data-testid="execution-result"]')).toHaveText(resultText ?? '')
  })

  test('an agent with no tools configured completes without attempting any tool calls', async ({ page }) => {
    await page.goto('/agents')

    await page.getByRole('button', { name: 'New Agent' }).click()
    await page.getByLabel('Name').fill('No-Tools Agent')
    await page.getByLabel('System Instructions').fill('You are a helpful assistant.')
    await page.getByLabel('AI Provider').selectOption({ index: 1 })
    await page.getByLabel('Model').selectOption({ index: 1 })
    await page.getByRole('button', { name: 'Create Agent' }).click()
    await page.getByRole('button', { name: 'Publish' }).click()

    await page.getByLabel('Objective').fill('What is 2+2?')
    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.getByText('Completed')).toBeVisible({ timeout: 30_000 })
    await expect(page.locator('[data-testid="tool-call-row"]')).toHaveCount(0)
  })
})
