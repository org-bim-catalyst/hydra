import { expect, test } from '@playwright/test'

/**
 * User Story 3 — version, compare, and restore prompt changes (specs/019-prompt-library-workspace
 * quickstart.md Scenario 3).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same constraint documented on PromptLifecycle.spec.ts.
 */

test.describe('Prompt versioning', () => {
  test('editing twice creates two new versions, comparing and restoring never deletes history', async ({ page }) => {
    await page.goto('/prompts')
    await page.locator('[data-testid="prompt-card"]').first().click()

    // Edit #1
    await page.getByLabel('User instructions').fill('First edit {{document}}.')
    await page.getByRole('button', { name: 'Save' }).click()

    // Edit #2
    await page.getByLabel('User instructions').fill('Second edit {{document}}.')
    await page.getByRole('button', { name: 'Save' }).click()

    await page.getByRole('tab', { name: 'Versions' }).click()

    const versionRows = page.locator('[data-testid="version-history-list"] li')
    await expect(versionRows).toHaveCount(3)

    // Compare v1 and v3
    await page.getByLabel('Select version 1 for comparison').check()
    await page.getByLabel('Select version 3 for comparison').check()
    await expect(page.locator('[data-testid="version-comparison"]')).toBeVisible()

    // Restore v1
    await versionRows.filter({ hasText: 'v1' }).getByRole('button', { name: 'Restore' }).click()
    await expect(page.locator('[data-testid="version-history-list"] li')).toHaveCount(4)
    await expect(page.getByLabel('User instructions')).toHaveValue(/First edit|document/)

    // Duplicate v2 into a new, independent prompt
    await versionRows.filter({ hasText: 'v2' }).getByRole('button', { name: 'Duplicate' }).click()
    await expect(page).toHaveURL(/\/prompts\/[0-9a-f-]+$/)
  })
})
