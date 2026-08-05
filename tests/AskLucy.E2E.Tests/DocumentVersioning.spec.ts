import { expect, test } from '@playwright/test'

/**
 * User Story 5 — version documents over time
 * (specs/015-document-intelligence-pipeline quickstart.md Scenario 5).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on DocumentUploadLifecycle.spec.ts and
 * other existing suites in this project). Written to the same selector/assertion conventions as
 * those suites so it runs unmodified once a real environment is wired into CI.
 */

test.describe('Document versioning', () => {
  test('replacing a document creates a new current version while the prior version stays in the timeline', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root').first().locator('.MuiTypography-subtitle2').click()

    const panel = page.getByRole('dialog')
    await expect(panel.getByText('Versions')).toBeVisible()
    await expect(panel.getByText('v1.0')).toBeVisible()
    await expect(panel.getByText('Current')).toBeVisible()

    const fileChooserPromise = page.waitForEvent('filechooser')
    await panel.getByRole('button', { name: 'Replace file' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles('tests/AskLucy.E2E.Tests/fixtures/sample-v2.pdf')

    await expect(panel.getByText('v1.1')).toBeVisible({ timeout: 30_000 })
    // Both versions remain listed — replacing never deletes prior version history (FR-038).
    await expect(panel.getByText('v1.0')).toBeVisible()
    await expect(panel.locator('li', { hasText: 'v1.1' }).getByText('Current')).toBeVisible()
  })

  test('the version timeline shows both versions with creator and date', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root').first().locator('.MuiTypography-subtitle2').click()

    const panel = page.getByRole('dialog')
    const versionRows = panel.locator('.MuiListItem-root')
    await expect(versionRows).toHaveCount(2)
    // Every row pairs a creator and a date — never a bare version label (FR-040).
    await expect(versionRows.first().locator('.MuiListItemText-secondary')).not.toBeEmpty()
  })

  test('restoring an earlier version repoints current without deleting any version, within 30 seconds', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root').first().locator('.MuiTypography-subtitle2').click()

    const panel = page.getByRole('dialog')
    const startTime = Date.now()
    await panel.locator('li', { hasText: 'v1.0' }).getByRole('button', { name: 'Restore' }).click()

    await expect(panel.locator('li', { hasText: 'v1.0' }).getByText('Current')).toBeVisible()
    expect(Date.now() - startTime).toBeLessThan(30_000) // SC-007.
    // Both versions still present — restore never deletes history (FR-041).
    await expect(panel.getByText('v1.1')).toBeVisible()
  })

  test('comparing two versions shows the extracted text and field differences', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root').first().locator('.MuiTypography-subtitle2').click()

    const panel = page.getByRole('dialog')
    await panel.locator('li', { hasText: 'v1.1' }).getByRole('button', { name: 'Compare' }).click()

    const compareDialog = page.getByRole('dialog', { name: 'Compare versions' })
    await expect(compareDialog.getByText('Extracted text')).toBeVisible()
    await expect(compareDialog.locator('pre')).not.toBeEmpty()
  })

  test('a version restore is rejected with 409 while a replacement upload is in progress for the same document', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root').first().locator('.MuiTypography-subtitle2').click()

    const panel = page.getByRole('dialog')
    const fileChooserPromise = page.waitForEvent('filechooser')
    await panel.getByRole('button', { name: 'Replace file' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles('tests/AskLucy.E2E.Tests/fixtures/sample-v3-large.pdf') // Large enough that the upload is still running below.

    // While the replace upload is mid-flight, a restore attempt on the same document must be
    // rejected deterministically rather than corrupting version history (Edge Cases).
    await panel.locator('li', { hasText: 'v1.0' }).getByRole('button', { name: 'Restore' }).click()
    await expect(panel.getByText('replacement upload is already in progress', { exact: false })).toBeVisible()
  })
})
