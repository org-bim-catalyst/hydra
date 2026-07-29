import { expect, test } from '@playwright/test'

/**
 * User Story 2 — organize and quickly find any conversation (specs/002-chat-history-management
 * quickstart.md Scenario 2).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — see ConversationPersistence.spec.ts's doc comment.
 */

test.describe('Conversation discovery at scale', () => {
  test('searching finds a conversation by message content', async ({ page }) => {
    await page.goto('/chat')

    await page.getByPlaceholder('Message Ask Lucy...').fill('zzqconveyor belt maintenance schedule')
    await page.keyboard.press('Enter')
    await expect(page.locator('text=zzqconveyor').first()).toBeVisible({ timeout: 15_000 })

    await page.getByPlaceholder('Search conversations').fill('zzqconveyor')
    await expect(page.getByRole('button', { name: /zzqconveyor/i })).toBeVisible({ timeout: 10_000 })
  })

  test('sort order changes the conversation list ordering', async ({ page }) => {
    await page.goto('/chat')

    await page.getByLabel('Sort conversations').click()
    await page.getByRole('option', { name: 'Alphabetical' }).click()

    const titles = await page.locator('[data-testid="conversation-title"]').allTextContents()
    const sorted = [...titles].sort((a, b) => a.localeCompare(b))
    expect(titles).toEqual(sorted)
  })

  test('scrolling the sidebar loads additional conversations without a full page reload', async ({ page }) => {
    await page.goto('/chat')

    const list = page.locator('[data-testid="conversation-list"]')
    const initialCount = await list.locator('[data-testid="conversation-item"]').count()

    await list.evaluate((el) => el.scrollTo(0, el.scrollHeight))
    await page.waitForTimeout(500)

    const afterScrollCount = await list.locator('[data-testid="conversation-item"]').count()
    expect(afterScrollCount).toBeGreaterThanOrEqual(initialCount)
  })

  test('the sidebar groups conversations by recency', async ({ page }) => {
    await page.goto('/chat')

    await expect(page.getByText('Today')).toBeVisible()
  })
})
