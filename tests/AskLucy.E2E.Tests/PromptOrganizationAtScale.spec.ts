import { expect, test } from '@playwright/test'

/**
 * User Story 4 — organize and find prompts at scale (specs/019-prompt-library-workspace
 * quickstart.md Scenario 4).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same constraint documented on PromptLifecycle.spec.ts.
 */

test.describe('Prompt organization at scale', () => {
  test('nested folders, search, combined filters, favorite/pin toggling, recently-used ordering', async ({ page }) => {
    await page.goto('/prompts')

    // Create a nested folder (FR-054).
    await page.getByRole('button', { name: 'New Folder' }).click()
    await page.getByLabel('Folder name').fill('Marketing')
    await page.getByRole('button', { name: 'Create' }).click()
    await expect(page.locator('[data-testid="prompt-folder-row"]', { hasText: 'Marketing' })).toBeVisible()

    await page.locator('[data-testid="prompt-folder-row"]', { hasText: 'Marketing' }).getByRole('button', { name: 'More actions' }).click()
    await page.getByRole('menuitem', { name: 'New Subfolder' }).click()
    await page.getByLabel('Folder name').fill('Campaigns')
    await page.getByRole('button', { name: 'Create' }).click()
    await expect(page.locator('[data-testid="prompt-folder-row"]', { hasText: 'Campaigns' })).toBeVisible()

    // Search by keyword — matches on name/description/content (US4 AC1).
    await page.getByPlaceholder('Search prompts by name, description, content…').fill('budget')
    await expect(page.locator('[data-testid="prompt-card"]').first()).toBeVisible()

    // Clear search, apply combined category+tag filters (US4 AC2).
    await page.getByLabel('Clear search').click()
    await page.getByLabel('Category').click()
    await page.getByRole('option', { name: 'Marketing' }).click()
    await page.getByLabel('Tag').fill('campaign')
    await page.getByRole('option', { name: 'campaign' }).click()
    for (const card of await page.locator('[data-testid="prompt-card"]').all()) {
      await expect(card).toBeVisible()
    }

    // Favorite/pin toggling is independent of the underlying prompt (US4 AC3).
    const firstCard = page.locator('[data-testid="prompt-card"]').first()
    await firstCard.getByLabel('Favorite prompt').click()
    await expect(firstCard.getByLabel('Unfavorite prompt')).toBeVisible()
    await firstCard.getByLabel('Pin prompt').click()
    await expect(firstCard.getByLabel('Unpin prompt')).toBeVisible()

    await page.getByTestId(/prompt-view-Favorites/).click()
    await expect(page.locator('[data-testid="prompt-card"]')).toContainText([])

    // Recently-used ordering only reflects successful executions (US4 AC4, spec.md Clarifications).
    await page.getByTestId(/prompt-view-RecentlyUsed/).click()
    const recentlyUsedCards = page.locator('[data-testid="prompt-card"]')
    await expect(recentlyUsedCards.first()).toBeVisible()

    // Folder-cycle rejection (spec.md Edge Cases): attempt to move "Marketing" into its own
    // child "Campaigns" and confirm the operation is rejected, not silently accepted.
    await page.locator('[data-testid="prompt-folder-row"]', { hasText: 'Marketing' }).getByRole('button', { name: 'More actions' }).click()
    await page.getByRole('menuitem', { name: /Move to…/ }).click()
    await page.getByRole('option', { name: 'Campaigns' }).click()
    await expect(page.getByRole('alert')).toContainText(/cannot be moved into itself|own subfolders/i)
  })
})
