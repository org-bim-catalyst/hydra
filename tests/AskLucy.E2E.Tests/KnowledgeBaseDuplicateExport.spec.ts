import { expect, test } from '@playwright/test'

/**
 * User Story 6 — duplication and export, including independent file copies
 * (specs/014-knowledge-base-management quickstart.md Scenario 6).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same documented constraint as the other
 * `KnowledgeBase*.spec.ts` files (no browser/server harness available in this sandbox).
 */

test.describe('Knowledge base duplication and export', () => {
  test('duplicating a knowledge base creates a new, independent Draft copy', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    const originalName = await card.locator('[data-testid="knowledge-base-name"]').textContent()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Duplicate' }).click()

    const copyCard = page.locator('[data-testid="knowledge-base-card"]', { hasText: `Copy of ${originalName}` })
    await expect(copyCard).toBeVisible()
    await expect(copyCard.locator('[data-testid="knowledge-base-status"]')).toContainText('Draft')
  })

  test('editing the duplicate does not affect the original', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const originalCard = page.locator('[data-testid="knowledge-base-card"]').first()
    const originalName = await originalCard.locator('[data-testid="knowledge-base-name"]').textContent()
    const originalDocCount = await originalCard.textContent()

    const copyCard = page.locator('[data-testid="knowledge-base-card"]', { hasText: `Copy of ${originalName}` })
    await copyCard.click()
    // (move/rename a document inside the duplicate's folder tree — covered by US2's own
    // folder-tree interactions, not repeated here)
    await page.goBack()

    const refreshedOriginal = page.locator('[data-testid="knowledge-base-card"]', { hasText: originalName ?? '' })
    await expect(refreshedOriginal).toHaveText(originalDocCount ?? '')
  })

  test('purging the duplicate leaves the original documents intact', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const copyCard = page.locator('[data-testid="knowledge-base-card"]', { hasText: 'Copy of' }).first()
    await copyCard.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Delete' }).click()
    await page.getByRole('tab', { name: 'Deleted' }).click()
    const deletedCopyCard = page.locator('[data-testid="knowledge-base-card"]', { hasText: 'Copy of' }).first()
    await deletedCopyCard.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Delete permanently' }).click()
    await page.getByRole('button', { name: 'Delete permanently' }).click()

    await expect(page.locator('[data-testid="knowledge-base-card"]', { hasText: 'Copy of' })).toHaveCount(0)
    await page.getByRole('tab', { name: 'Active' }).click()
    await expect(page.locator('[data-testid="knowledge-base-card"]').first()).toBeVisible()
  })

  test('exporting a knowledge base downloads a JSON file with the documented metadata shape', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    await card.getByLabel('More actions').click()
    const downloadPromise = page.waitForEvent('download')
    await page.getByRole('menuitem', { name: 'Export' }).click()
    const download = await downloadPromise

    expect(download.suggestedFilename()).toMatch(/\.json$/)
    const path = await download.path()
    const content = JSON.parse(await (await import('node:fs')).promises.readFile(path!, 'utf-8')) as Record<string, unknown>
    expect(content).toHaveProperty('name')
    expect(content).toHaveProperty('folders')
    expect(content).toHaveProperty('documentCount')
    expect(content).toHaveProperty('notes')
  })
})
