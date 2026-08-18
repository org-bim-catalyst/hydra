import { expect, test } from '@playwright/test'

/**
 * User Story 2 — User reviews and manages what Lucy remembers (specs/018-ai-memory-system
 * quickstart.md Scenario 2, spec.md US2 AC1–AC4, SC-002).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend/frontend deployment and an
 * authenticated session with at least one existing memory, mirroring every other spec in this
 * directory's caveat. Run via `npm test` from this directory against a real deployment
 * (`E2E_BASE_URL` env var).
 */

const MEMORY_CONTENT_FRAGMENT = process.env.E2E_MEMORY_FIXTURE_FRAGMENT ?? 'PostgreSQL'

test.describe('User reviews and manages what Lucy remembers', () => {
  test('view, search, edit, and delete a memory in under 30 seconds (SC-002)', async ({ page }) => {
    const startedAt = Date.now()

    await page.goto('/memory')
    await expect(page.getByRole('heading', { name: 'Memory Center' })).toBeVisible()

    await page.getByPlaceholder('Search memories').fill(MEMORY_CONTENT_FRAGMENT)
    const card = page.getByTestId('memory-card').first()
    await expect(card).toBeVisible({ timeout: 10_000 })
    await expect(card).toContainText(MEMORY_CONTENT_FRAGMENT)

    await card.getByRole('button', { name: 'Edit memory' }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    await dialog.getByLabel('Content').fill(`${MEMORY_CONTENT_FRAGMENT} — updated by E2E test`)
    await dialog.getByRole('button', { name: 'Save' }).click()
    await expect(dialog).not.toBeVisible()
    await expect(page.getByText('updated by E2E test')).toBeVisible()

    await card.getByRole('button', { name: 'Delete memory' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await page.getByRole('button', { name: 'Delete' }).click()
    await expect(page.getByText('updated by E2E test')).not.toBeVisible()

    expect(Date.now() - startedAt).toBeLessThan(30_000)
  })

  test('filtering by category narrows the list', async ({ page }) => {
    await page.goto('/memory')

    await page.getByLabel('Category').click()
    await page.getByRole('option', { name: 'Personal fact' }).click()

    const cards = page.getByTestId('memory-card')
    const count = await cards.count()
    for (let i = 0; i < count; i++) {
      await expect(cards.nth(i)).toContainText('Personal fact')
    }
  })
})
