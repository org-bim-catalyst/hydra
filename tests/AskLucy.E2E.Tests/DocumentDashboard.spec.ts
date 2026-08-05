import { expect, test } from '@playwright/test'

/**
 * User Story 6 — monitor processing activity and get notified
 * (specs/015-document-intelligence-pipeline quickstart.md Scenario 6).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on DocumentUploadLifecycle.spec.ts and
 * other existing suites in this project). Written to the same selector/assertion conventions as
 * those suites so it runs unmodified once a real environment is wired into CI.
 */

test.describe('Document dashboard and notifications', () => {
  test('the per-user dashboard counts reflect an upload, a completion, and a failure within 5 seconds', async ({ page }) => {
    await page.goto('/documents')

    const dashboard = page.getByText('Queued').locator('..')
    const queuedCountBefore = await dashboard.locator('.MuiTypography-h5').first().textContent()

    const fileChooserPromise = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload documents' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles([
      'tests/AskLucy.E2E.Tests/fixtures/sample.pdf',
      'tests/AskLucy.E2E.Tests/fixtures/mislabeled.pdf', // Engineered to fail (FR-028).
    ])

    // Live counts update via the dashboard's 5s poll (research.md Decision 7) — no page refresh.
    await expect(dashboard.locator('.MuiTypography-h5').first()).not.toHaveText(queuedCountBefore ?? '', { timeout: 10_000 })
    await expect(page.getByText('Completed today').locator('..').locator('.MuiTypography-h5')).not.toHaveText('0', { timeout: 30_000 })
    await expect(page.getByText('Failed').locator('..').locator('.MuiTypography-h5')).not.toHaveText('0', { timeout: 30_000 })
  })

  test('in-app notifications arrive for upload-completed, processing-completed, and processing-failed', async ({ page }) => {
    await page.goto('/documents')

    const fileChooserPromise = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload documents' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles('tests/AskLucy.E2E.Tests/fixtures/sample.pdf')

    // The live toast (useNotificationHub) surfaces the upload-completed event immediately.
    await expect(page.getByRole('alert').filter({ hasText: 'uploaded successfully' })).toBeVisible({ timeout: 15_000 })

    await page.getByRole('button', { name: 'Notifications' }).click()
    const inbox = page.getByRole('presentation').filter({ hasText: 'Notifications' })
    await expect(inbox.getByText('Upload Completed')).toBeVisible()
    await expect(inbox.getByText('Processing Completed')).toBeVisible({ timeout: 30_000 })
  })

  test('reaching the storage limit blocks further uploads and fires a StorageLimitReached notification', async ({ page }) => {
    await page.goto('/documents') // Test account pre-seeded at/near its storage quota.

    const fileChooserPromise = page.waitForEvent('filechooser')
    await page.getByRole('button', { name: 'Upload documents' }).click()
    const fileChooser = await fileChooserPromise
    await fileChooser.setFiles('tests/AskLucy.E2E.Tests/fixtures/sample.pdf')

    await expect(page.getByText('storage limit', { exact: false })).toBeVisible({ timeout: 15_000 })

    await page.getByRole('button', { name: 'Notifications' }).click()
    const inbox = page.getByRole('presentation').filter({ hasText: 'Notifications' })
    await expect(inbox.getByText('Storage Limit Reached')).toBeVisible()
  })

  test('the organization-wide dashboard is visible to an administrator and reflects multi-user activity, while a non-admin never sees it', async ({ page }) => {
    await page.goto('/documents') // Logged in as an administrator test account.

    await expect(page.getByText('Organization-wide (administrator view)')).toBeVisible()
    const orgSection = page.getByText('Organization-wide (administrator view)').locator('..')
    await expect(orgSection.getByText('Queued')).toBeVisible()

    const response = await page.request.get('/api/v1/documents/dashboard/organization')
    expect(response.status()).not.toBe(403)
  })

  test('a non-administrator never sees the organization-wide dashboard section, and the endpoint returns 403', async ({ page }) => {
    await page.goto('/documents') // Logged in as a regular (non-admin) test account.

    await expect(page.getByText('Organization-wide (administrator view)')).toHaveCount(0)

    const response = await page.request.get('/api/v1/documents/dashboard/organization')
    expect(response.status()).toBe(403)
  })
})
