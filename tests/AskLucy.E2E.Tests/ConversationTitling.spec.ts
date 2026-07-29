import { expect, test } from '@playwright/test'

/**
 * User Story 6 — automatic and manual conversation titles (specs/002-chat-history-management
 * quickstart.md Scenario 6).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — see ConversationPersistence.spec.ts's doc comment.
 */

test.describe('Conversation titling', () => {
  test('a new conversation receives an automatic title from its first message', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()

    await page.getByPlaceholder('Message Ask Lucy...').fill('How do I reset my password?')
    await page.keyboard.press('Enter')

    await expect(page.locator('[data-testid="conversation-title"]').first()).toContainText('How do I reset my password', {
      timeout: 5_000,
    })
  })

  test('a manual rename is never overwritten by later auto-titling', async ({ page }) => {
    await page.goto('/chat')

    const firstItem = page.locator('[data-testid="conversation-item"]').first()
    await firstItem.hover()
    await firstItem.getByLabel('Rename chat').click()
    await page.keyboard.type('My custom title')
    await page.keyboard.press('Enter')

    await firstItem.click()
    await page.getByPlaceholder('Message Ask Lucy...').fill('Another message')
    await page.keyboard.press('Enter')

    await expect(page.locator('[data-testid="conversation-title"]').first()).toContainText('My custom title')
  })
})
