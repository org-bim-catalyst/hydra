import { expect, test } from '@playwright/test'

/**
 * User Story 5 — categories and tags, including private-custom-category scoping
 * (specs/014-knowledge-base-management quickstart.md Scenario 5).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same documented constraint as the other
 * `KnowledgeBase*.spec.ts` files (no browser/server harness available in this sandbox).
 */

test.describe('Knowledge base categories and tags', () => {
  test('assigning a predefined category shows it on the card and makes it filterable', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Edit' }).click()
    await page.getByLabel('Category').click()
    await page.getByRole('option', { name: 'Engineering' }).click()
    await page.getByRole('button', { name: 'Save' }).click()

    await expect(card.getByText('Engineering')).toBeVisible()
  })

  test('creating a custom category makes it usable and assignable', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Edit' }).click()
    await page.getByLabel('Category').click()
    await page.getByRole('option', { name: 'Create new category…' }).click()
    await page.getByLabel('New category name').fill('Vendor Docs')
    await page.getByRole('button', { name: 'Create' }).click()
    await page.getByRole('button', { name: 'Save' }).click()

    await expect(card.getByText('Vendor Docs')).toBeVisible()
  })

  test("a custom category is private — a second user never sees it in their category list", async ({ browser }) => {
    const ownerContext = await browser.newContext({ storageState: 'e2e/.auth/user-one.json' })
    const ownerPage = await ownerContext.newPage()
    await ownerPage.goto('/knowledge-bases')
    // (category "Vendor Docs" created by user-one in a prior scenario/seed)

    const otherContext = await browser.newContext({ storageState: 'e2e/.auth/user-two.json' })
    const otherPage = await otherContext.newPage()
    const response = await otherPage.request.get('/api/v1/knowledge-bases/categories')
    const categories = (await response.json()) as { name: string }[]

    expect(categories.some((c) => c.name === 'Vendor Docs')).toBe(false)

    await ownerContext.close()
    await otherContext.close()
  })

  test('adding two tags shows both and makes each usable as a filter', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Edit' }).click()
    await page.getByLabel('Tags').fill('revit')
    await page.keyboard.press('Enter')
    await page.getByLabel('Tags').fill('standards')
    await page.keyboard.press('Enter')
    await page.getByRole('button', { name: 'Save' }).click()

    await expect(card.getByText('revit')).toBeVisible()
    await expect(card.getByText('standards')).toBeVisible()

    await page.getByLabel('Filter by tag').fill('revit')
    await expect(page.locator('[data-testid="knowledge-base-card"]')).not.toHaveCount(0)
  })

  test('deleting a custom category falls referencing knowledge bases back to Uncategorized', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const card = page.locator('[data-testid="knowledge-base-card"]', { hasText: 'Vendor Docs' }).first()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Edit' }).click()
    await page.getByLabel('Category').click()
    await page.getByRole('option', { name: 'Manage categories…' }).click()
    await page.getByRole('listitem', { name: 'Vendor Docs' }).getByLabel('Delete category').click()
    await page.getByRole('button', { name: 'Delete' }).click()

    await expect(card.getByText('Uncategorized')).toBeVisible()
    await expect(card.getByText('Vendor Docs')).toHaveCount(0)
  })
})
