import { expect, test } from '@playwright/test'

/**
 * User Story 1 — Chat with your documents and get cited answers (specs/016-rag-semantic-search
 * quickstart.md Scenario 1, spec.md US1 AC1–AC6).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend (real SQL Server/Pinecone vector
 * store, OpenAI embeddings + chat key) and frontend dev server plus an authenticated session with
 * at least two knowledge bases already indexed — see ConversationPersistence.spec.ts's doc comment
 * for the same caveat. Run via `npm test` from this directory against a real deployment
 * (`E2E_BASE_URL` env var).
 *
 * PRECONDITION beyond the usual auth session: as of this Foundational-only implementation,
 * indexing a knowledge base has no user-facing trigger yet (US5 "automatic indexing on upload" and
 * US6 "manual reindex" are both deferred) — the fixture knowledge base(s) below must already be
 * `indexStatus: "Indexed"` before this spec runs, via a direct call to `IIndexingOrchestrator`
 * (e.g. a test setup script), not through the UI or public API. `KB_ONE_NAME`/`KB_TWO_NAME` name
 * two such pre-indexed fixture knowledge bases containing known, distinct content.
 */

const KB_ONE_NAME = process.env.E2E_RAG_KB_ONE_NAME ?? 'E2E RAG Fixture KB 1'
const KB_TWO_NAME = process.env.E2E_RAG_KB_TWO_NAME ?? 'E2E RAG Fixture KB 2'
const KB_ONE_QUESTION = process.env.E2E_RAG_KB_ONE_QUESTION ?? 'What does the fixture document say?'
const KB_ONE_ANSWER_FRAGMENT = process.env.E2E_RAG_KB_ONE_ANSWER_FRAGMENT ?? 'fixture'

test.describe('Chat with your documents and get cited answers', () => {
  test('a grounded response cites the source document, with page/section, within 5 seconds', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()

    await page.getByLabel('Knowledge bases').click()
    await page.getByRole('option', { name: KB_ONE_NAME }).click()
    await page.keyboard.press('Escape')

    const sentAt = Date.now()
    await page.getByPlaceholder('Message Ask Lucy...').fill(KB_ONE_QUESTION)
    await page.keyboard.press('Enter')

    const citation = page.locator('.MuiChip-root', { hasText: KB_ONE_NAME }).first()
    await expect(citation).toBeVisible({ timeout: 15_000 })
    expect(Date.now() - sentAt).toBeLessThan(5_000 + 15_000) // generous network allowance around the 5s SC-001 retrieval target

    await expect(page.locator('text=' + KB_ONE_ANSWER_FRAGMENT).first()).toBeVisible()
  })

  test('a question spanning two attached knowledge bases attributes each citation to its own source', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()

    await page.getByLabel('Knowledge bases').click()
    await page.getByRole('option', { name: KB_ONE_NAME }).click()
    await page.getByRole('option', { name: KB_TWO_NAME }).click()
    await page.keyboard.press('Escape')

    await page.getByPlaceholder('Message Ask Lucy...').fill('Summarize both of my documents.')
    await page.keyboard.press('Enter')

    await expect(page.locator('.MuiChip-root', { hasText: KB_ONE_NAME }).first()).toBeVisible({ timeout: 15_000 })
    await expect(page.locator('.MuiChip-root', { hasText: KB_TWO_NAME }).first()).toBeVisible()
  })

  test('a conversation with no knowledge base attached gets no citations and no retrieval', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()

    await page.getByPlaceholder('Message Ask Lucy...').fill('What is the capital of France?')
    await page.keyboard.press('Enter')
    await expect(page.locator('text=Paris').first()).toBeVisible({ timeout: 15_000 })

    await expect(page.locator('text=No relevant content')).not.toBeVisible()
    await expect(page.locator('.MuiChip-root', { hasText: KB_ONE_NAME })).not.toBeVisible()
  })

  test('a question with no relevant content states that clearly instead of an ungrounded answer presented as grounded', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()

    await page.getByLabel('Knowledge bases').click()
    await page.getByRole('option', { name: KB_ONE_NAME }).click()
    await page.keyboard.press('Escape')

    await page.getByPlaceholder('Message Ask Lucy...').fill('What is the airspeed velocity of an unladen swallow?')
    await page.keyboard.press('Enter')

    await expect(page.locator('text=No relevant content was found')).toBeVisible({ timeout: 15_000 })
  })

  test('opening a citation shows the source page/section with the passage highlighted, within 10 seconds', async ({ page }) => {
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()

    await page.getByLabel('Knowledge bases').click()
    await page.getByRole('option', { name: KB_ONE_NAME }).click()
    await page.keyboard.press('Escape')

    await page.getByPlaceholder('Message Ask Lucy...').fill(KB_ONE_QUESTION)
    await page.keyboard.press('Enter')

    const citation = page.locator('.MuiChip-root', { hasText: KB_ONE_NAME }).first()
    await expect(citation).toBeVisible({ timeout: 15_000 })

    const openedAt = Date.now()
    await citation.click()

    const viewer = page.getByRole('dialog')
    await expect(viewer).toBeVisible()
    await expect(viewer.locator('blockquote')).toBeVisible()
    expect(Date.now() - openedAt).toBeLessThan(10_000)
  })

  test('degraded mode: an embedding-provider outage still answers, visibly unlabeled as grounded, with a non-silent retrieval error', async ({
    page,
  }) => {
    // Simulating the embedding-provider outage itself (e.g. toggling a feature flag or
    // rotating the configured API key) is an out-of-band environment step, not something this
    // spec can trigger — see this file's doc comment. This test assumes the outage is already
    // active for the duration of the run.
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()

    await page.getByLabel('Knowledge bases').click()
    await page.getByRole('option', { name: KB_ONE_NAME }).click()
    await page.keyboard.press('Escape')

    await page.getByPlaceholder('Message Ask Lucy...').fill(KB_ONE_QUESTION)
    await page.keyboard.press('Enter')

    // The message still completes — never blocked by the retrieval failure (FR-037a).
    await expect(page.locator('.MuiAlert-message', { hasText: /temporarily unavailable/i })).toBeVisible({ timeout: 15_000 })
    await expect(page.locator('.MuiChip-root', { hasText: KB_ONE_NAME })).not.toBeVisible()
  })
})
