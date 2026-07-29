import { expect, test } from '@playwright/test'

/**
 * User Story 4 — delete, Recently Deleted, and permanent delete (specs/002-chat-history-management
 * quickstart.md Scenario 4).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — see ConversationPersistence.spec.ts's doc comment.
 */

test.describe('Conversation deletion lifecycle', () => {
  test('deleting a conversation moves it to Recently Deleted, and it can be restored', async ({ page }) => {
    await page.goto('/chat')

    const firstItem = page.locator('[data-testid="conversation-item"]').first()
    const title = await firstItem.locator('[data-testid="conversation-title"]').textContent()
    await firstItem.hover()
    await firstItem.getByLabel('Delete chat').click()

    await page.getByRole('button', { name: 'Recently Deleted' }).click()
    await expect(page.locator('[data-testid="conversation-item"]')).toContainText(title ?? '')

    const deletedItem = page.locator('[data-testid="conversation-item"]').first()
    await deletedItem.hover()
    await deletedItem.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Restore' }).click()

    await page.getByRole('button', { name: 'All' }).click()
    await expect(page.locator('[data-testid="conversation-item"]')).toContainText(title ?? '')
  })

  test('permanent delete requires confirmation and is irreversible', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'Recently Deleted' }).click()

    const item = page.locator('[data-testid="conversation-item"]').first()
    const title = await item.locator('[data-testid="conversation-title"]').textContent()
    await item.hover()
    await item.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Delete permanently' }).click()

    await expect(page.getByText('Permanently delete this conversation?')).toBeVisible()
    await page.getByRole('button', { name: 'Delete permanently' }).click()

    await expect(page.locator('[data-testid="conversation-item"]')).not.toContainText(title ?? '')
  })
})
