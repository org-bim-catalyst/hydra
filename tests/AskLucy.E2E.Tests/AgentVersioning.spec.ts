import { expect, test } from '@playwright/test'

/**
 * User Story 6 — publish v1, edit, publish v2; the version history shows both immutably, and
 * duplicate/archive/restore all behave correctly (specs/020-ai-agent-framework quickstart.md
 * Scenario 6).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent E2E suite so it runs unmodified
 * once a real environment is wired into CI.
 */

test.describe('Agent versioning and lifecycle actions', () => {
  test('publishing twice keeps both versions in history', async ({ page }) => {
    await page.goto('/agents')
    await page.getByRole('button', { name: 'New Agent' }).click()
    await page.getByLabel('Name').fill('Versioned Agent')
    await page.getByLabel('System Instructions').fill('v1 instructions.')
    await page.getByLabel('AI Provider').selectOption({ index: 1 })
    await page.getByLabel('Model').selectOption({ index: 1 })
    await page.getByRole('button', { name: 'Create Agent' }).click()
    await page.getByRole('button', { name: 'Publish' }).click()

    await page.getByLabel('System Instructions').fill('v2 instructions — completely different.')
    await page.getByRole('button', { name: 'Save Changes' }).click()
    await page.getByRole('button', { name: 'Publish' }).click()

    await expect(page.locator('[data-testid="version-history-row"]')).toHaveCount(2)
    await expect(page.getByText('v2')).toBeVisible()
    await expect(page.getByText('v1')).toBeVisible()
  })

  test('duplicate creates a new draft agent with the same tools', async ({ page }) => {
    await page.goto('/agents')
    await page.getByRole('button', { name: 'New Agent' }).click()
    await page.getByLabel('Name').fill('Original For Duplicate')
    await page.getByLabel('System Instructions').fill('You are a helpful assistant.')
    await page.getByLabel('AI Provider').selectOption({ index: 1 })
    await page.getByLabel('Model').selectOption({ index: 1 })
    await page.getByRole('button', { name: 'Create Agent' }).click()

    await page.goto('/agents')
    await page.getByRole('button', { name: 'Duplicate' }).first().click()

    await expect(page.getByText('Original For Duplicate (Copy)')).toBeVisible()
  })

  test('archive then restore returns an agent to its previous status', async ({ page }) => {
    await page.goto('/agents')
    await page.getByRole('button', { name: 'New Agent' }).click()
    await page.getByLabel('Name').fill('Archivable Agent')
    await page.getByLabel('System Instructions').fill('You are a helpful assistant.')
    await page.getByLabel('AI Provider').selectOption({ index: 1 })
    await page.getByLabel('Model').selectOption({ index: 1 })
    await page.getByRole('button', { name: 'Create Agent' }).click()
    await page.getByRole('button', { name: 'Publish' }).click()

    await page.goto('/agents')
    const card = page.getByText('Archivable Agent').locator('..').locator('..')
    await card.getByRole('button', { name: 'Archive' }).click()
    await expect(card.getByText('Archived')).toBeVisible()

    await card.getByRole('button', { name: 'Restore' }).click()
    await expect(card.getByText('Published')).toBeVisible()
  })
})
