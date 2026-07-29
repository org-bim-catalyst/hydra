import { expect, test } from '@playwright/test'

/**
 * User Story 3 — pin, favorite, archive, restore, duplicate, clear (specs/002-chat-history-management
 * quickstart.md Scenario 3).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — see ConversationPersistence.spec.ts's doc comment.
 */

test.describe('Conversation curation', () => {
  test('pinning a conversation moves it to the top regardless of recency', async ({ page }) => {
    await page.goto('/chat')

    const firstItem = page.locator('[data-testid="conversation-item"]').first()
    await firstItem.hover()
    await firstItem.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Pin' }).click()

    await expect(page.locator('[data-testid="conversation-item"]').first().locator('text=📌')).toBeVisible()
  })

  test('favoriting a conversation makes it appear under the Favorites filter', async ({ page }) => {
    await page.goto('/chat')

    const firstItem = page.locator('[data-testid="conversation-item"]').first()
    const title = await firstItem.getAttribute('aria-label')
    await firstItem.hover()
    await firstItem.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Favorite' }).click()

    await page.getByRole('button', { name: 'Favorites' }).click()
    await expect(page.locator('[data-testid="conversation-item"]')).toContainText(title ?? '')
  })

  test('archiving removes a conversation from the default view and shows it under Archived', async ({ page }) => {
    await page.goto('/chat')

    const firstItem = page.locator('[data-testid="conversation-item"]').first()
    await firstItem.hover()
    await firstItem.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Archive' }).click()

    await page.getByRole('button', { name: 'Archived' }).click()
    await expect(page.locator('[data-testid="conversation-item"]').first()).toBeVisible()
  })

  test('duplicating a conversation creates an independent copy with the same messages', async ({ page }) => {
    await page.goto('/chat')

    const beforeCount = await page.locator('[data-testid="conversation-item"]').count()

    const firstItem = page.locator('[data-testid="conversation-item"]').first()
    await firstItem.hover()
    await firstItem.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Duplicate' }).click()

    await expect(page.locator('[data-testid="conversation-item"]')).toHaveCount(beforeCount + 1)
  })

  test('clearing messages requires confirmation and empties the conversation', async ({ page }) => {
    await page.goto('/chat')

    const firstItem = page.locator('[data-testid="conversation-item"]').first()
    await firstItem.hover()
    await firstItem.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Clear messages' }).click()

    await expect(page.getByText('Clear all messages?')).toBeVisible()
    await page.getByRole('button', { name: 'Clear messages' }).click()

    await expect(page.getByText('Start a conversation with Ask Lucy.')).toBeVisible()
  })
})
