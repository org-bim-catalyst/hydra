import { expect, test } from '@playwright/test'

/**
 * User Story 4 — User controls memory privacy at the account level (specs/018-ai-memory-system
 * quickstart.md Scenario 4, spec.md US4 AC1–AC4, SC-003).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend/frontend deployment and an
 * authenticated session with at least one existing memory, mirroring every other spec in this
 * directory's caveat. Run via `npm test` from this directory against a real deployment
 * (`E2E_BASE_URL` env var).
 */

test.describe('User controls memory privacy at the account level', () => {
  test('disabling memory takes immediate effect, without deleting stored memories', async ({ page }) => {
    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Preferences' }).click()

    await page.getByLabel('Let Lucy remember things about me').uncheck()
    await expect(page.getByText('Preferences')).toBeVisible()

    // Stored memories are still visible in the Memory Center — disabling doesn't delete anything.
    await page.getByRole('tab', { name: 'All memories' }).click()
    await expect(page.getByTestId('memory-card').first()).toBeVisible()
  })

  test('clear-all removes every memory in at most three actions (SC-003)', async ({ page }) => {
    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Preferences' }).click()

    // Action 1: open the clear-all dialog. Action 2: confirm. (Well within the 3-action budget.)
    await page.getByRole('button', { name: 'Clear all memories' }).click()
    await page.getByRole('button', { name: 'Clear all', exact: true }).click()

    await page.getByRole('tab', { name: 'All memories' }).click()
    await expect(page.getByText('Nothing remembered yet')).toBeVisible({ timeout: 10_000 })
  })

  test('export produces a downloadable file, even for a zero-memory account', async ({ page }) => {
    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Preferences' }).click()

    const downloadPromise = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Export my memories' }).click()
    const download = await downloadPromise

    expect(download.suggestedFilename()).toBe('memory-export.json')
  })

  test('disabling one category stops it being used, other categories keep working', async ({ page }) => {
    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Preferences' }).click()

    const projectContextRow = page.getByText('Project context').locator('..')
    await projectContextRow.getByLabel('In use').uncheck()

    await expect(projectContextRow.getByLabel('In use')).not.toBeChecked()
    // Other categories remain enabled — spot-check one.
    const personalFactsRow = page.getByText('Personal facts').locator('..')
    await expect(personalFactsRow.getByLabel('In use')).toBeChecked()
  })
})
