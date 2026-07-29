import { expect, test } from '@playwright/test'

/**
 * User Story 5 — export a conversation (specs/002-chat-history-management quickstart.md
 * Scenario 5).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — see ConversationPersistence.spec.ts's doc comment.
 */

test.describe('Conversation export', () => {
  test('exporting a conversation downloads a JSON file with the full message history', async ({ page }) => {
    await page.goto('/chat')

    const firstItem = page.locator('[data-testid="conversation-item"]').first()
    await firstItem.hover()
    await firstItem.getByLabel('More actions').click()

    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('menuitem', { name: 'Export' }).click()])

    expect(download.suggestedFilename()).toMatch(/\.json$/)
  })
})
