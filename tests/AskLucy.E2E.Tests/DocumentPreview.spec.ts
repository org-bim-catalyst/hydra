import { expect, test } from '@playwright/test'

/**
 * User Story 7 — preview documents without downloading
 * (specs/015-document-intelligence-pipeline quickstart.md Scenario 7).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on DocumentUploadLifecycle.spec.ts and
 * other existing suites in this project). Written to the same selector/assertion conventions as
 * those suites so it runs unmodified once a real environment is wired into CI.
 */

test.describe('Document inline preview', () => {
  test('a completed PDF renders a page image preview inline, without downloading', async ({ page }) => {
    const downloadPromise = page.waitForEvent('download', { timeout: 3000 }).catch(() => null)

    await page.goto('/documents')
    await page.locator('.MuiCard-root', { hasText: 'sample.pdf' }).getByText('sample.pdf').click()

    const panel = page.getByRole('dialog', { name: /sample\.pdf details/ })
    await expect(panel.getByText('Preview')).toBeVisible()
    await expect(panel.locator('img[alt^="Preview of"]')).toBeVisible({ timeout: 15_000 })

    expect(await downloadPromise).toBeNull()
  })

  test('a completed DOCX renders its extracted structure (headings/paragraphs/tables/lists) inline', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root', { hasText: 'report.docx' }).getByText('report.docx').click()

    const panel = page.getByRole('dialog', { name: /report\.docx details/ })
    await expect(panel.locator('.MuiListItem-root').first()).toBeVisible({ timeout: 15_000 })
  })

  test('a completed image renders a thumbnail preview inline', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root', { hasText: 'photo.png' }).getByText('photo.png').click()

    const panel = page.getByRole('dialog', { name: /photo\.png details/ })
    await expect(panel.locator('img[alt^="Preview of"]')).toBeVisible({ timeout: 15_000 })
  })

  test('a completed Markdown document renders directly, with no server round trip for the artifact itself', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root', { hasText: 'notes.md' }).getByText('notes.md').click()

    const panel = page.getByRole('dialog', { name: /notes\.md details/ })
    // Rendered as actual HTML (react-markdown), not shown as a raw "# Heading" text block.
    await expect(panel.locator('h1, h2, h3').first()).toBeVisible({ timeout: 15_000 })
  })

  test('a document type with no preview support clearly offers download instead of erroring', async ({ page }) => {
    await page.goto('/documents')
    await page.locator('.MuiCard-root', { hasText: 'data.json' }).getByText('data.json').click()

    const panel = page.getByRole('dialog', { name: /data\.json details/ })
    await expect(panel.getByText('No preview available for this document.')).toBeVisible({ timeout: 15_000 })
    await expect(panel.getByRole('button', { name: 'Download instead' })).toBeEnabled()
  })
})
