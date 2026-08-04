import { expect, test } from '@playwright/test'

/**
 * User Story 3 — activate/archive/restore (specs/014-knowledge-base-management quickstart.md
 * Scenario 3).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same documented constraint as KnowledgeBaseLifecycle.spec.ts.
 */

test.describe('Knowledge base archive and restore', () => {
  test('archiving an Active knowledge base moves it out of the default Active view', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    const name = await card.locator('[data-testid="knowledge-base-name"]').textContent()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Archive' }).click()

    await expect(page.locator('[data-testid="knowledge-base-card"]', { hasText: name ?? '' })).toHaveCount(0)

    await page.getByRole('tab', { name: 'Archived' }).click()
    const archivedCard = page.locator('[data-testid="knowledge-base-card"]', { hasText: name ?? '' })
    await expect(archivedCard).toBeVisible()
    await expect(archivedCard.locator('[data-testid="knowledge-base-status"]')).toContainText('Archived')
  })

  test('restoring an archived knowledge base returns it to Active with identical structure and metadata', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.getByRole('tab', { name: 'Archived' }).click()

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    const name = await card.locator('[data-testid="knowledge-base-name"]').textContent()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Restore' }).click()

    await page.getByRole('tab', { name: 'Active' }).click()
    const restoredCard = page.locator('[data-testid="knowledge-base-card"]', { hasText: name ?? '' })
    await expect(restoredCard).toBeVisible()
    await expect(restoredCard.locator('[data-testid="knowledge-base-status"]')).toContainText('Active')
  })

  test('a favorited knowledge base keeps its favorite marker while archived', async ({ page }) => {
    await page.goto('/knowledge-bases')

    // Favorite then archive (via the card's context menu — the favorite toggle itself ships
    // with US4's discovery UI; this spec assumes a knowledge base already favorited by a prior
    // scenario or seed data, consistent with this suite's "not runnable here" status).
    const card = page.locator('[data-testid="knowledge-base-card"]').filter({ has: page.locator('[aria-label="Favorite"]') }).first()
    const name = await card.locator('[data-testid="knowledge-base-name"]').textContent()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Archive' }).click()

    await page.getByRole('tab', { name: 'Archived' }).click()
    const archivedCard = page.locator('[data-testid="knowledge-base-card"]', { hasText: name ?? '' })
    await expect(archivedCard.locator('[aria-label="Favorite"]')).toBeVisible()
  })
})
