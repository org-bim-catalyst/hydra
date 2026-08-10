import { expect, test } from '@playwright/test'

/**
 * User Story 2 — test a prompt before relying on it: streamed execution, usage/cost display,
 * save-as-test-case, and capability-mismatch blocking (specs/019-prompt-library-workspace
 * quickstart.md Scenario 2).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same constraint documented on PromptLifecycle.spec.ts and
 * specs/002-chat-history-management's ConversationPersistence.spec.ts.
 */

test.describe('Prompt Testing Workspace', () => {
  test('executing a prompt streams output and displays token usage and cost', async ({ page }) => {
    await page.goto('/prompts')
    await page.locator('[data-testid="prompt-card"]').first().click()
    await page.getByRole('tab', { name: 'Test' }).click()

    await page.getByLabel('document').fill('The quarterly report shows steady growth.')
    await page.getByLabel('Provider').selectOption({ label: 'OpenAI' })
    await page.getByLabel('Model').selectOption({ index: 0 })
    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.locator('[data-testid="execution-output"]')).not.toBeEmpty()
    await expect(page.locator('[data-testid="execution-token-usage"]')).toBeVisible()
    await expect(page.locator('[data-testid="execution-cost"]')).toBeVisible()
  })

  test('leaving a required variable blank blocks execution before any provider call', async ({ page }) => {
    await page.goto('/prompts')
    await page.locator('[data-testid="prompt-card"]').first().click()
    await page.getByRole('tab', { name: 'Test' }).click()

    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.getByText(/is required/i)).toBeVisible()
    await expect(page.locator('[data-testid="execution-output"]')).toBeEmpty()
  })

  test('saving a completed execution as a test case makes it reusable', async ({ page }) => {
    await page.goto('/prompts')
    await page.locator('[data-testid="prompt-card"]').first().click()
    await page.getByRole('tab', { name: 'Test' }).click()

    await page.getByLabel('document').fill('Sample input')
    await page.getByRole('button', { name: 'Run' }).click()
    await expect(page.locator('[data-testid="execution-output"]')).not.toBeEmpty()

    await page.getByRole('button', { name: 'Save as test case' }).click()
    await page.getByLabel('Test case name').fill('Happy path')
    await page.getByRole('button', { name: 'Save', exact: true }).click()

    await page.getByRole('tab', { name: 'Test cases' }).click()
    await expect(page.getByText('Happy path')).toBeVisible()
  })

  test('a capability-incompatible model is excluded or flagged before execution', async ({ page }) => {
    await page.goto('/prompts')
    await page.locator('[data-testid="prompt-card"]', { hasText: 'Vision-required prompt' }).click()
    await page.getByRole('tab', { name: 'Test' }).click()

    await page.getByLabel('Model').selectOption({ label: 'A model without vision support' })

    await expect(page.getByText(/does not support required capabilities/i)).toBeVisible()
  })
})
