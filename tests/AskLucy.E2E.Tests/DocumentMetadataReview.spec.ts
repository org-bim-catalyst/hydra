import { expect, test } from '@playwright/test'

/**
 * User Story 3 — review and correct extracted content
 * (specs/015-document-intelligence-pipeline quickstart.md Scenario 3).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on DocumentUploadLifecycle.spec.ts and
 * other existing suites in this project).
 */

test.describe('Document metadata review and correction', () => {
  test('editing metadata, overriding classification, and adding a tag all persist', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root').first().locator('.MuiTypography-subtitle2').click()

    const panel = page.getByRole('dialog')
    await expect(panel.getByText('Metadata')).toBeVisible()

    await panel.getByLabel('Title').fill('Updated Report Title')
    await panel.getByRole('button', { name: 'Save metadata' }).click()
    await expect(panel.getByText('Edited')).toBeVisible()

    await panel.getByRole('combobox').first().click()
    await page.getByRole('option', { name: 'Legal' }).click()
    await expect(panel.getByText('User override')).toBeVisible()

    await panel.getByLabel('Add a tag').fill('Reviewed')
    await panel.getByRole('button', { name: 'Add' }).click()
    await expect(panel.getByText('Reviewed')).toBeVisible()
  })

  test('a stale concurrent edit shows the out-of-date warning instead of being rejected', async ({ browser }) => {
    const context1 = await browser.newContext()
    const context2 = await browser.newContext()
    const page1 = await context1.newPage()
    const page2 = await context2.newPage()

    await page1.goto('/documents')
    await page1.locator('.MuiCard-root').first().locator('.MuiTypography-subtitle2').click()
    await page2.goto('/documents')
    await page2.locator('.MuiCard-root').first().locator('.MuiTypography-subtitle2').click()

    // Both tabs loaded the same rowVersion; the first save wins outright, the second must merge
    // and warn rather than reject (FR-031a, research.md Decision 9).
    await page1.getByRole('dialog').getByLabel('Title').fill('First Tab Title')
    await page1.getByRole('dialog').getByRole('button', { name: 'Save metadata' }).click()
    await expect(page1.getByRole('dialog').getByText('Edited')).toBeVisible()

    await page2.getByRole('dialog').getByLabel('Title').fill('Second Tab Title')
    await page2.getByRole('dialog').getByRole('button', { name: 'Save metadata' }).click()
    await expect(page2.getByRole('dialog').getByText('your view was out of date', { exact: false })).toBeVisible()

    await context1.close()
    await context2.close()
  })
})
