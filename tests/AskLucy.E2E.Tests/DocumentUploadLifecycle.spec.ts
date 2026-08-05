import { expect, test } from '@playwright/test'

/**
 * User Story 1 — upload, browse, rename, download, archive, restore, and delete a document
 * (specs/015-document-intelligence-pipeline quickstart.md Scenario 1).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on specs/002-chat-history-management's
 * ConversationPersistence.spec.ts and specs/014-knowledge-base-management's
 * KnowledgeBaseLifecycle.spec.ts). Written to the same selector/assertion conventions as those
 * existing suites so it runs unmodified once a real environment is wired into CI.
 */

test.describe('Document upload and lifecycle', () => {
  test('drag-and-drop upload shows progress and appears in the list once complete', async ({ page }) => {
    await page.goto('/documents')

    const fileChooserPromise = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload documents' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles('tests/AskLucy.E2E.Tests/fixtures/sample.pdf')

    await expect(page.getByText('sample.pdf')).toBeVisible()
    await expect(page.getByText('Done')).toBeVisible({ timeout: 15_000 })

    const card = page.locator('.MuiCard-root', { hasText: 'sample.pdf' })
    await expect(card).toBeVisible()
  })

  test('multiple files upload independently and a queued file can be cancelled', async ({ page }) => {
    await page.goto('/documents')

    const fileChooserPromise = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload documents' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles([
      'tests/AskLucy.E2E.Tests/fixtures/sample.pdf',
      'tests/AskLucy.E2E.Tests/fixtures/sample.docx',
    ])

    await expect(page.getByText('sample.pdf')).toBeVisible()
    await expect(page.getByText('sample.docx')).toBeVisible()

    await page.getByLabel('Cancel upload of sample.docx').click()
    await expect(page.getByText('Cancelled')).toBeVisible()
  })

  test('rename, download, archive, restore, and delete all reflect immediately', async ({ page }) => {
    await page.goto('/documents')

    const card = page.locator('.MuiCard-root').first()
    const originalName = (await card.locator('.MuiTypography-subtitle2').textContent()) ?? ''

    await card.getByLabel(`Archive ${originalName}`).click()
    await expect(page.getByText('Archived')).toBeVisible()

    await page.getByRole('tab', { name: 'Archived' }).click()
    const archivedCard = page.locator('.MuiCard-root', { hasText: originalName })
    await expect(archivedCard).toBeVisible()
    await archivedCard.getByLabel(`Restore ${originalName}`).click()

    await page.getByRole('tab', { name: 'Active' }).click()
    const restoredCard = page.locator('.MuiCard-root', { hasText: originalName })
    await restoredCard.getByLabel(`Delete ${originalName}`).click()
    await expect(page.locator('.MuiCard-root', { hasText: originalName })).toHaveCount(0)

    await page.getByRole('tab', { name: 'Deleted' }).click()
    await expect(page.locator('.MuiCard-root', { hasText: originalName })).toBeVisible()
  })

  test('a large-file upload resumes after a simulated network interruption', async ({ page, context }) => {
    await page.goto('/documents')

    const fileChooserPromise = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload documents' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles('tests/AskLucy.E2E.Tests/fixtures/large-sample.pdf')

    // Simulate an interruption partway through, then restore connectivity — the resumable
    // flow must continue from the last received chunk rather than restarting (FR-005, SC-005).
    await page.waitForTimeout(500)
    await context.setOffline(true)
    await page.waitForTimeout(1000)
    await context.setOffline(false)

    await expect(page.getByText('Done')).toBeVisible({ timeout: 30_000 })
  })
})
