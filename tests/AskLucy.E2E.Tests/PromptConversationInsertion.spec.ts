import { expect, test } from '@playwright/test'

/**
 * User Story 5 — use a saved prompt inside a live conversation (specs/019-prompt-library-workspace
 * quickstart.md Scenario 5).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server + OpenAI key) and
 * frontend dev server plus an authenticated session — same caveat as
 * ConversationPersistence.spec.ts/RegressionMatrix.spec.ts. Run via `npm test` from this
 * directory against a real deployment (`E2E_BASE_URL` env var).
 */

test.describe('Insert prompt into conversation', () => {
  test('insert into an existing conversation preserves prior context and model selection', async ({ page }) => {
    await page.goto('/chat')

    // Send an ordinary message first so there is prior context to preserve.
    await page.getByPlaceholder('Message Ask Lucy...').fill('What is our expense reimbursement policy?')
    await page.keyboard.press('Enter')
    await expect(page.locator('text=/OpenAI/').first()).toBeVisible({ timeout: 15_000 })

    // Insert a saved prompt (US5 AC1/AC2).
    await page.getByRole('button', { name: 'Insert saved prompt' }).click()
    await page.getByPlaceholder('Search prompts…').fill('Summarize a document')
    await page.getByRole('button', { name: 'Summarize a document' }).click()

    await page.getByLabel('document').fill('The Q3 budget report.')
    await page.getByRole('button', { name: 'Insert' }).click()

    await expect(page.getByRole('dialog')).toBeHidden()
    // The resolved prompt text becomes the new user message (US5 AC2).
    await expect(page.locator('text=The Q3 budget report.')).toBeVisible()
    // The conversation's existing provider/model attribution still shows on the new reply too —
    // proves the send used the conversation's own selection, not a different one (FR-080).
    await expect(page.locator('text=/OpenAI/').nth(1)).toBeVisible({ timeout: 15_000 })
    // The prior turn is still present — prior context was preserved, not replaced.
    await expect(page.locator('text=What is our expense reimbursement policy?')).toBeVisible()
  })

  test('a capability-incompatible conversation model blocks insertion before anything is sent', async ({ page }) => {
    await page.goto('/chat')

    await page.getByRole('button', { name: 'Insert saved prompt' }).click()
    await page.getByPlaceholder('Search prompts…').fill('Describe this image')
    await page.getByRole('button', { name: 'Describe this image' }).click()

    await expect(page.getByText(/does not support required capabilities/i)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Insert' })).toBeDisabled()
  })
})
