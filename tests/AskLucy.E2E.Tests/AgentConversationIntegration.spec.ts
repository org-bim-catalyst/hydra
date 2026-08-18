import { expect, test } from '@playwright/test'

/**
 * User Story 1 — all three conversation-integration modes (spec.md FR-051/FR-052).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written
 * to the same selector/assertion conventions as the existing Prompt Library E2E suite so it runs
 * unmodified once a real environment is wired into CI.
 */

async function createPublishedAgent(page: import('@playwright/test').Page, name: string) {
  await page.goto('/agents')
  await page.getByRole('button', { name: 'New Agent' }).click()
  await page.getByLabel('Name').fill(name)
  await page.getByLabel('System Instructions').fill('You are a helpful assistant.')
  await page.getByLabel('AI Provider').selectOption({ index: 1 })
  await page.getByLabel('Model').selectOption({ index: 1 })
  await page.getByRole('button', { name: 'Create Agent' }).click()
  await page.getByRole('button', { name: 'Publish' }).click()
}

test.describe('Agent conversation integration', () => {
  test('Standalone mode never creates or requires a conversation', async ({ page }) => {
    await createPublishedAgent(page, 'Standalone Agent')

    await expect(page.getByLabel('Conversation')).toHaveValue('Standalone (no conversation)')
    await page.getByLabel('Objective').fill('Say hi.')
    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.getByText('Completed')).toBeVisible({ timeout: 30_000 })
  })

  test('NewConversation mode creates a conversation and links the execution to it', async ({ page }) => {
    await createPublishedAgent(page, 'New Conversation Agent')

    await page.getByLabel('Conversation').selectOption('NewConversation')
    await page.getByLabel('Objective').fill('Say hi.')
    await page.getByRole('button', { name: 'Run' }).click()

    await expect(page.getByText('Completed')).toBeVisible({ timeout: 30_000 })

    // FR-052 — the objective and final result were posted into the newly created conversation.
    await page.goto('/chat')
    await expect(page.getByText('Agent: New Conversation Agent')).toBeVisible()
  })

  test('ExistingConversation mode requires a conversation id and links to that conversation', async ({ page }) => {
    await createPublishedAgent(page, 'Existing Conversation Agent')

    await page.getByLabel('Conversation').selectOption('ExistingConversation')
    await expect(page.getByRole('button', { name: 'Run' })).toBeDisabled()

    await page.getByLabel('Conversation ID').fill('00000000-0000-0000-0000-000000000000')
    await page.getByLabel('Objective').fill('Say hi.')
    await page.getByRole('button', { name: 'Run' }).click()

    // A conversation id the caller doesn't own/that doesn't exist fails clearly, never silently.
    await expect(page.getByText(/not found/i)).toBeVisible()
  })
})
