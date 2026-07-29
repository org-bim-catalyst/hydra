import { expect, test } from '@playwright/test'

/**
 * User Story 1 — persistence across sessions (specs/002-chat-history-management
 * quickstart.md Scenario 1).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server + OpenAI
 * key) and frontend dev server plus an authenticated session — see RegressionMatrix.spec.ts's
 * doc comment for the same caveat. Run via `npm test` from this directory against a real
 * deployment (`E2E_BASE_URL` env var).
 */

test.describe('Conversation persistence across sessions', () => {
  test('a new conversation and its message survive a page reload', async ({ page }) => {
    await page.goto('/chat')

    await page.getByPlaceholder('Message Ask Lucy...').fill('What is the capital of France?')
    await page.keyboard.press('Enter')
    await expect(page.locator('text=Paris').first()).toBeVisible({ timeout: 15_000 })

    const conversationTitle = await page.locator('[aria-label="Rename chat"]').first().getAttribute('aria-label')
    expect(conversationTitle).toBeTruthy()

    await page.reload()

    await expect(page.locator('text=What is the capital of France?')).toBeVisible()
    await expect(page.locator('text=Paris').first()).toBeVisible()
  })

  test('creating several conversations does not hit an artificial limit', async ({ page }) => {
    await page.goto('/chat')

    for (let i = 0; i < 5; i++) {
      await page.getByRole('button', { name: 'New chat' }).click()
      await page.getByPlaceholder('Message Ask Lucy...').fill(`Test message ${i}`)
      await page.keyboard.press('Enter')
      await expect(page.locator(`text=Test message ${i}`)).toBeVisible()
    }

    await page.reload()
    const items = page.getByRole('button').filter({ hasText: 'Test message' })
    await expect(items).toHaveCount(5)
  })

  test('an assistant reply displays its provider/model metadata', async ({ page }) => {
    await page.goto('/chat')

    await page.getByPlaceholder('Message Ask Lucy...').fill('Say hello in one word.')
    await page.keyboard.press('Enter')

    await expect(page.locator('text=/OpenAI/').first()).toBeVisible({ timeout: 15_000 })
  })
})
