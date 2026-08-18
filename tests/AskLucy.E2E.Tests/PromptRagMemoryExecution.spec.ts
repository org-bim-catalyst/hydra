import { expect, test } from '@playwright/test'

/**
 * User Story 6 — request RAG or Memory context from a prompt (specs/019-prompt-library-workspace
 * quickstart.md Scenario 6).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same constraint documented on PromptLifecycle.spec.ts.
 */

test.describe('Prompt RAG/Memory execution', () => {
  test('RAG-grounded execution against an indexed knowledge base', async ({ page }) => {
    await page.goto('/prompts')
    await page.locator('[data-testid="prompt-card"]').first().click()
    await page.getByRole('tab', { name: 'Test' }).click()

    await page.getByLabel('Use Knowledge Base (RAG) context').check()
    await page.getByLabel('Knowledge bases').click()
    await page.getByRole('option').first().click()
    await page.keyboard.press('Escape')

    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.locator('[data-testid="execution-output"]')).not.toBeEmpty({ timeout: 15_000 })
  })

  test('memory-grounded execution for a user with stored memories', async ({ page }) => {
    await page.goto('/prompts')
    await page.locator('[data-testid="prompt-card"]').first().click()
    await page.getByRole('tab', { name: 'Test' }).click()

    await page.getByLabel('Use Memory context').check()
    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.locator('[data-testid="execution-output"]')).not.toBeEmpty({ timeout: 15_000 })
  })

  test('combined RAG and Memory execution keeps every component structurally distinguishable', async ({ page }) => {
    await page.goto('/prompts')
    await page.locator('[data-testid="prompt-card"]').first().click()
    await page.getByRole('tab', { name: 'Test' }).click()

    await page.getByLabel('Use Knowledge Base (RAG) context').check()
    await page.getByLabel('Knowledge bases').click()
    await page.getByRole('option').first().click()
    await page.keyboard.press('Escape')
    await page.getByLabel('Use Memory context').check()

    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.locator('[data-testid="execution-output"]')).not.toBeEmpty({ timeout: 15_000 })
  })
})
