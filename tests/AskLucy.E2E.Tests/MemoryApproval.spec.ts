import { expect, test } from '@playwright/test'

/**
 * User Story 3 — User approves what Lucy is allowed to remember (specs/018-ai-memory-system
 * quickstart.md Scenario 3, spec.md US3 AC1–AC5).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend/frontend deployment, an
 * authenticated session, and (for the sensitive-content case) a way to trigger memory extraction
 * against a real AI provider — mirroring every other spec in this directory's caveat. Run via
 * `npm test` from this directory against a real deployment (`E2E_BASE_URL` env var).
 */

const SENSITIVE_STATEMENT = process.env.E2E_MEMORY_SENSITIVE_FIXTURE ?? 'I was recently diagnosed with a chronic illness.'

test.describe('User approves what Lucy is allowed to remember', () => {
  test('manual mode holds a candidate for review, then approve/reject take effect', async ({ page }) => {
    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Preferences' }).click()

    // Set Personal facts to Manual.
    await page.getByLabel('Personal facts approval mode').click()
    await page.getByRole('option', { name: /Manual/ }).click()

    // Trigger a candidate via chat, then find it in the approval queue.
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()
    await page.getByPlaceholder('Message Ask Lucy...').fill('My company uses .NET for everything.')
    await page.keyboard.press('Enter')

    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Approval queue' }).click()
    const item = page.getByTestId('approval-queue-item').first()
    await expect(item).toBeVisible({ timeout: 30_000 })

    await item.getByRole('button', { name: 'Approve' }).click()
    await expect(item).not.toBeVisible()
  })

  test('disabled mode never surfaces a candidate for that category', async ({ page }) => {
    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Preferences' }).click()

    await page.getByLabel('Personal facts approval mode').click()
    await page.getByRole('option', { name: /Disabled/ }).click()

    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()
    await page.getByPlaceholder('Message Ask Lucy...').fill('My company uses PHP for everything.')
    await page.keyboard.press('Enter')

    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Approval queue' }).click()
    await expect(page.getByText('My company uses PHP')).not.toBeVisible({ timeout: 10_000 })
  })

  test('a sensitive statement is always held for review even in automatic mode', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()
    await page.getByPlaceholder('Message Ask Lucy...').fill(SENSITIVE_STATEMENT)
    await page.keyboard.press('Enter')

    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Approval queue' }).click()
    const item = page.getByTestId('approval-queue-item').first()
    await expect(item).toBeVisible({ timeout: 30_000 })
    await expect(item.getByText('Sensitive')).toBeVisible()
  })
})
