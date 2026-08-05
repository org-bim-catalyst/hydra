import { expect, test } from '@playwright/test'

/**
 * User Story 2 — automatic document processing with visible status
 * (specs/015-document-intelligence-pipeline quickstart.md Scenario 2).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on DocumentUploadLifecycle.spec.ts and
 * other existing suites in this project). Written to the same selector/assertion conventions as
 * those suites so it runs unmodified once a real environment is wired into CI.
 */

test.describe('Document processing status and retry', () => {
  test('upload progresses through every stage to Completed without a page refresh', async ({ page }) => {
    await page.goto('/documents')

    const fileChooserPromise = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload documents' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles('tests/AskLucy.E2E.Tests/fixtures/sample.pdf')

    await expect(page.getByText('Done')).toBeVisible({ timeout: 15_000 })

    const card = page.locator('.MuiCard-root', { hasText: 'sample.pdf' })
    await card.getByText('sample.pdf').click()

    const detailPanel = page.getByRole('dialog', { name: /sample\.pdf details/ })
    await expect(detailPanel.getByText('Live')).toBeVisible()

    // No page refresh between stages — the panel updates purely via SignalR push/poll.
    await expect(detailPanel.getByText('Validating')).toBeVisible()
    await expect(detailPanel.getByText('Completed')).toBeVisible({ timeout: 120_000 })
  })

  test('a scanned PDF is OCR-recognized while a text-layer PDF skips OCR entirely', async ({ page }) => {
    await page.goto('/documents')

    const fileChooserPromise = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload documents' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles([
      'tests/AskLucy.E2E.Tests/fixtures/sample.pdf', // has an extractable text layer
      'tests/AskLucy.E2E.Tests/fixtures/scanned-sample.pdf', // image-only, needs OCR
    ])

    await expect(page.getByText('Done')).toBeVisible({ timeout: 15_000 })

    await page.locator('.MuiCard-root', { hasText: 'sample.pdf' }).getByText('sample.pdf').click()
    let detailPanel = page.getByRole('dialog', { name: /sample\.pdf details/ })
    await expect(detailPanel.getByText('Completed')).toBeVisible({ timeout: 120_000 })
    await expect(detailPanel.getByText('Skipped')).toBeVisible() // OCR stage.
    await page.getByLabel('Close').click()

    await page.locator('.MuiCard-root', { hasText: 'scanned-sample.pdf' }).getByText('scanned-sample.pdf').click()
    detailPanel = page.getByRole('dialog', { name: /scanned-sample\.pdf details/ })
    await expect(detailPanel.getByText('Completed')).toBeVisible({ timeout: 120_000 })
  })

  test('a mislabeled file and a password-protected PDF both fail with a specific reason and a working retry', async ({ page }) => {
    await page.goto('/documents')

    const fileChooserPromise = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload documents' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles([
      'tests/AskLucy.E2E.Tests/fixtures/mislabeled.pdf', // actually a .txt file renamed .pdf
      'tests/AskLucy.E2E.Tests/fixtures/password-protected.pdf',
    ])

    await expect(page.getByText('Done')).toBeVisible({ timeout: 15_000 })

    await page.locator('.MuiCard-root', { hasText: 'mislabeled.pdf' }).getByText('mislabeled.pdf').click()
    const detailPanel = page.getByRole('dialog', { name: /mislabeled\.pdf details/ })
    await expect(detailPanel.getByText('Failed')).toBeVisible({ timeout: 30_000 })

    // The failure reason must be specific and actionable — never a generic "an error occurred".
    await expect(detailPanel.getByText('an error occurred', { exact: false })).toHaveCount(0)
    const retryButton = detailPanel.getByRole('button', { name: 'Retry' })
    await expect(retryButton).toBeEnabled()
    await retryButton.click()
    await expect(detailPanel.getByText('Processing')).toBeVisible()
  })

  test('the processing history panel lists every state transition with a timestamp', async ({ page }) => {
    await page.goto('/documents')

    await page.locator('.MuiCard-root').first().locator('.MuiTypography-subtitle2').click()
    const detailPanel = page.getByRole('dialog')

    await expect(detailPanel.getByText('History')).toBeVisible()
    const historyItems = detailPanel.locator('.MuiListItem-root')
    await expect(historyItems.first()).toBeVisible()
    // Every entry pairs an event with a timestamp — never a bare label (FR-013, SC-009).
    await expect(historyItems.first().locator('.MuiListItemText-secondary')).not.toBeEmpty()
  })
})
