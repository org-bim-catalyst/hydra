import { expect, test } from '@playwright/test'

/**
 * User Story 1 — Lucy remembers me across conversations (specs/018-ai-memory-system
 * quickstart.md Scenario 1, spec.md US1 AC1–AC3).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server, an OpenAI
 * embeddings + chat key, Hangfire's server actually processing jobs) and frontend dev server plus
 * an authenticated session, mirroring ChatWithCitations.spec.ts's caveat. Run via `npm test` from
 * this directory against a real deployment (`E2E_BASE_URL` env var).
 *
 * PRECONDITION: memory extraction is asynchronous (an enqueued Hangfire job, tasks.md T032/T034) —
 * this spec polls for the fact to become usable rather than asserting immediately after sending it,
 * since there is no synchronous "extraction complete" signal exposed to the client.
 */

const FACT = process.env.E2E_MEMORY_FACT ?? 'I use PostgreSQL for all my personal projects.'
const RECALL_QUESTION = process.env.E2E_MEMORY_RECALL_QUESTION ?? 'What database do I use for my personal projects?'
const RECALL_ANSWER_FRAGMENT = process.env.E2E_MEMORY_RECALL_ANSWER_FRAGMENT ?? 'PostgreSQL'

test.describe('Lucy remembers me across conversations', () => {
  test('a fact stated in one conversation is reflected, unprompted, in a later new conversation', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()

    await page.getByPlaceholder('Message Ask Lucy...').fill(FACT)
    await page.keyboard.press('Enter')
    await expect(page.getByText(FACT)).toBeVisible({ timeout: 15_000 })

    // Background extraction + (default Automatic mode) approval happens asynchronously — poll a
    // brand-new conversation until the fact is usable rather than a fixed sleep.
    await expect(async () => {
      await page.goto('/chat')
      await page.getByRole('button', { name: 'New chat' }).click()
      await page.getByPlaceholder('Message Ask Lucy...').fill(RECALL_QUESTION)
      await page.keyboard.press('Enter')
      await expect(page.locator('text=' + RECALL_ANSWER_FRAGMENT).first()).toBeVisible({ timeout: 15_000 })
    }).toPass({ timeout: 60_000 })

    // FR-014 — the "why does Lucy know this" trace for the response that just used the memory.
    const indicator = page.getByRole('button', { name: 'Lucy remembered this' })
    await expect(indicator).toBeVisible()
    await indicator.click()
    await expect(page.getByText(FACT, { exact: false })).toBeVisible()
  })
})
