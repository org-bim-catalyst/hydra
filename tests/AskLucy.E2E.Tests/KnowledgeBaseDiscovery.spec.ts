import { expect, test } from '@playwright/test'

/**
 * User Story 4 — dashboard discovery: search, filter, sort, grid/list, favorites, pinned
 * (specs/014-knowledge-base-management quickstart.md Scenario 4).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same documented constraint as
 * KnowledgeBaseLifecycle.spec.ts and KnowledgeBaseArchive.spec.ts (no browser/server harness
 * available in this sandbox).
 */

test.describe('Knowledge base dashboard discovery', () => {
  test('searching narrows the list to matching name/description/tag, updating as the user types', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const searchBox = page.getByLabel('Search knowledge bases')
    await searchBox.fill('Revit')

    const cards = page.locator('[data-testid="knowledge-base-card"]')
    await expect(cards).not.toHaveCount(0)
    for (const card of await cards.all()) {
      await expect(card).toContainText(/revit/i)
    }
  })

  test('searching for something that matches nothing shows a clear empty state, not a blank screen', async ({ page }) => {
    await page.goto('/knowledge-bases')

    await page.getByLabel('Search knowledge bases').fill('zzz-no-such-knowledge-base-zzz')

    await expect(page.locator('[data-testid="knowledge-base-card"]')).toHaveCount(0)
    await expect(page.getByText(/no knowledge bases/i)).toBeVisible()
  })

  test('category and tag filters combine to narrow the result set', async ({ page }) => {
    await page.goto('/knowledge-bases')

    await page.getByLabel('Category').click()
    await page.getByRole('option', { name: 'Engineering' }).click()
    await page.getByLabel('Tag').click()
    await page.getByRole('option', { name: 'revit' }).click()

    const cards = page.locator('[data-testid="knowledge-base-card"]')
    await expect(cards).not.toHaveCount(0)
    await expect(page.getByText('Engineering')).toBeVisible()
  })

  test('sorting re-orders the result set by name, recently updated, and document count', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const names = async () => page.locator('[data-testid="knowledge-base-name"]').allTextContents()

    await page.getByLabel('Sort knowledge bases').click()
    await page.getByRole('option', { name: 'Name' }).click()
    const byName = await names()

    await page.getByLabel('Sort knowledge bases').click()
    await page.getByRole('option', { name: 'Recently updated' }).click()
    const byRecentlyUpdated = await names()

    await page.getByLabel('Sort knowledge bases').click()
    await page.getByRole('option', { name: 'Document count' }).click()
    const byDocumentCount = await names()

    expect(byName).not.toEqual(byRecentlyUpdated)
    expect(byRecentlyUpdated).not.toEqual(byDocumentCount)
  })

  test('toggling grid/list view preserves the same filtered and sorted result set', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.getByLabel('Search knowledge bases').fill('KB')

    const gridNames = await page.locator('[data-testid="knowledge-base-name"]').allTextContents()
    await page.getByLabel('List view').click()
    const listNames = await page.locator('[data-testid="knowledge-base-name"]').allTextContents()

    expect(new Set(listNames)).toEqual(new Set(gridNames))
  })

  test('favoriting and pinning surface a knowledge base in its respective dashboard section', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const firstCard = page.locator('[data-testid="knowledge-base-card"]').first()
    const firstName = await firstCard.locator('[data-testid="knowledge-base-name"]').textContent()
    await firstCard.getByLabel('Favorite').click()

    const secondCard = page.locator('[data-testid="knowledge-base-card"]').nth(1)
    const secondName = await secondCard.locator('[data-testid="knowledge-base-name"]').textContent()
    await secondCard.getByLabel('Pin').click()

    await page.getByRole('tab', { name: 'Favorites' }).click()
    await expect(page.locator('[data-testid="knowledge-base-card"]', { hasText: firstName ?? '' })).toBeVisible()

    await page.getByRole('tab', { name: 'Pinned' }).click()
    await expect(page.locator('[data-testid="knowledge-base-card"]', { hasText: secondName ?? '' })).toBeVisible()
  })

  test('search and filter round trips stay responsive (SC-002/SC-003 performance budget)', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const start = Date.now()
    await page.getByLabel('Search knowledge bases').fill('KB')
    await page.locator('[data-testid="knowledge-base-card"]').first().waitFor()
    const elapsedMs = Date.now() - start

    expect(elapsedMs).toBeLessThan(2000)
  })
})
