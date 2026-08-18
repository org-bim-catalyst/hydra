import { expect, test } from '@playwright/test'

/**
 * User Story 6 — Lucy resolves contradictory memories (specs/018-ai-memory-system
 * quickstart.md Scenario 6, spec.md US6 AC1–AC3, clarified 2026-08-09).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend/frontend deployment, an
 * authenticated session, and a real AI provider to actually classify conflicts — mirroring every
 * other spec in this directory's caveat. Run via `npm test` from this directory against a real
 * deployment (`E2E_BASE_URL` env var).
 */

test.describe('Lucy resolves contradictory memories', () => {
  test('a direct contradiction updates the memory in place, without interrupting the chat', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()
    await page.getByPlaceholder('Message Ask Lucy...').fill('I use Angular for my frontend work.')
    await page.keyboard.press('Enter')
    await expect(page.getByText('I use Angular')).toBeVisible({ timeout: 15_000 })

    // The contradicting statement, later — the response must still arrive normally.
    await page.getByPlaceholder('Message Ask Lucy...').fill('Actually, I moved to React a while back.')
    const sentAt = Date.now()
    await page.keyboard.press('Enter')
    await expect(page.getByText('moved to React')).toBeVisible({ timeout: 15_000 })
    expect(Date.now() - sentAt).toBeLessThan(20_000) // never blocked/delayed by conflict detection

    // The Memory Center reflects the update, with history.
    await page.goto('/memory')
    await page.getByPlaceholder('Search memories').fill('React')
    await page.getByTestId('memory-card').first().getByRole('button', { name: 'Edit memory' }).click()
    await expect(page.getByRole('dialog')).toContainText('React')
  })

  test('an ambiguous conflict never interrupts the live turn and resolves asynchronously via the Memory Center', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()
    await page.getByPlaceholder('Message Ask Lucy...').fill('I work on residential construction projects.')
    await page.keyboard.press('Enter')
    await expect(page.getByText('residential construction')).toBeVisible({ timeout: 15_000 })

    await page.getByPlaceholder('Message Ask Lucy...').fill('I also work on some commercial projects now.')
    const sentAt = Date.now()
    await page.keyboard.press('Enter')
    await expect(page.getByText('commercial projects')).toBeVisible({ timeout: 15_000 })
    expect(Date.now() - sentAt).toBeLessThan(20_000)

    // Resolved later, asynchronously, via the notification feed.
    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Notifications' }).click()
    await page.getByText(/possible conflict/i).click()

    const dialog = page.getByRole('dialog', { name: 'Lucy noticed a possible conflict' })
    await expect(dialog).toBeVisible()
    await dialog.getByRole('button', { name: 'Keep both' }).click()
    await expect(dialog).not.toBeVisible()
  })
})
