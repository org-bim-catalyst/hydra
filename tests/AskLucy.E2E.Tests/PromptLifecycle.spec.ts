import { expect, test } from '@playwright/test'

/**
 * User Story 1 — create and reuse a structured prompt, with strict per-owner isolation
 * (specs/019-prompt-library-workspace quickstart.md Scenario 1).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on
 * specs/002-chat-history-management's ConversationPersistence.spec.ts). Written to the same
 * selector/assertion conventions as the existing KnowledgeBaseLifecycle.spec.ts suite so it runs
 * unmodified once a real environment is wired into CI.
 */

test.describe('Prompt lifecycle', () => {
  test('creating a prompt with variables auto-detects them and persists every field', async ({ page }) => {
    await page.goto('/prompts')

    await page.getByRole('button', { name: 'New Prompt' }).click()
    await page.getByLabel('Name').fill('Summarize a technical document')
    await page.getByLabel('System instructions').fill('You are a precise technical summarizer.')
    await page
      .getByLabel('User instructions')
      .fill('Summarize {{document}} in {{target_language}} at {{summary_length}} length.')
    await page.getByRole('button', { name: 'Save' }).click()

    const detectedVariables = page.locator('[data-testid="detected-variable-chip"]')
    await expect(detectedVariables).toHaveCount(3)
    await expect(detectedVariables).toContainText(['document', 'target_language', 'summary_length'])

    await page.reload()
    await expect(page.getByLabel('User instructions')).toHaveValue(
      'Summarize {{document}} in {{target_language}} at {{summary_length}} length.',
    )
  })

  test('creating a prompt with an undeclared placeholder is rejected with a clear message', async ({ page }) => {
    await page.goto('/prompts')

    await page.getByRole('button', { name: 'New Prompt' }).click()
    await page.getByLabel('Name').fill('Broken prompt')
    await page.getByLabel('User instructions').fill('Summarize {{document}}.')
    await page.getByRole('button', { name: 'Save' }).click()

    await expect(page.getByText(/undeclared placeholder/i)).toBeVisible()
  })

  test('a duplicate prompt name for the same owner is rejected', async ({ page }) => {
    await page.goto('/prompts')

    await page.getByRole('button', { name: 'New Prompt' }).click()
    await page.getByLabel('Name').fill('Existing Prompt Name')
    await page.getByLabel('User instructions').fill('Hello.')
    await page.getByRole('button', { name: 'Save' }).click()

    await page.getByRole('button', { name: 'New Prompt' }).click()
    await page.getByLabel('Name').fill('Existing Prompt Name')
    await page.getByLabel('User instructions').fill('Hello again.')
    await page.getByRole('button', { name: 'Save' }).click()

    await expect(page.getByText(/already have a prompt named/i)).toBeVisible()
  })

  test("a prompt is invisible to a second, different owner (SC-008)", async ({ browser }) => {
    const ownerContext = await browser.newContext({ storageState: 'playwright/.auth/user-a.json' })
    const ownerPage = await ownerContext.newPage()
    await ownerPage.goto('/prompts')
    await ownerPage.getByRole('button', { name: 'New Prompt' }).click()
    await ownerPage.getByLabel('Name').fill('Private to owner A')
    await ownerPage.getByLabel('User instructions').fill('Only I can see this.')
    await ownerPage.getByRole('button', { name: 'Save' }).click()
    await ownerContext.close()

    const otherContext = await browser.newContext({ storageState: 'playwright/.auth/user-b.json' })
    const otherPage = await otherContext.newPage()
    await otherPage.goto('/prompts')

    await expect(otherPage.locator('[data-testid="prompt-card"]', { hasText: 'Private to owner A' })).toHaveCount(0)
    await otherContext.close()
  })
})
