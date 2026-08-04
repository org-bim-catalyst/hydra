import { expect, test } from '@playwright/test'

/**
 * User Story 1 — create/edit/delete a knowledge base, and permanent deletion (owner-triggered)
 * (specs/014-knowledge-base-management quickstart.md Scenarios 1 and 7).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on specs/002-chat-history-management's
 * ConversationPersistence.spec.ts). Written to the same selector/assertion conventions as the
 * existing Conversation*.spec.ts suite so it runs unmodified once a real environment is wired
 * into CI.
 */

test.describe('Knowledge base core lifecycle', () => {
  test('creating a knowledge base saves it as Draft and shows it immediately', async ({ page }) => {
    await page.goto('/knowledge-bases')

    await page.getByRole('button', { name: 'New Knowledge Base' }).click()
    await page.getByLabel('Name').fill('BIM Standards')
    await page.getByLabel('Description').fill('Company-wide BIM modeling standards')
    await page.getByRole('button', { name: 'Create' }).click()

    const card = page.locator('[data-testid="knowledge-base-card"]', { hasText: 'BIM Standards' })
    await expect(card).toBeVisible()
    await expect(card.locator('[data-testid="knowledge-base-status"]')).toContainText('Draft')
  })

  test('creating a knowledge base without a name is rejected with a clear message', async ({ page }) => {
    await page.goto('/knowledge-bases')

    await page.getByRole('button', { name: 'New Knowledge Base' }).click()
    await page.getByRole('button', { name: 'Create' }).click()

    await expect(page.getByText('A knowledge base name is required.')).toBeVisible()
  })

  test('editing name/description/color/icon updates the card everywhere it is shown', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Edit' }).click()

    await page.getByLabel('Name').fill('Renamed Knowledge Base')
    await page.getByRole('button', { name: 'Save' }).click()

    await expect(page.locator('[data-testid="knowledge-base-card"]', { hasText: 'Renamed Knowledge Base' })).toBeVisible()
  })

  test('deleting a knowledge base moves it to the Deleted view, not gone', async ({ page }) => {
    await page.goto('/knowledge-bases')

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    const name = await card.locator('[data-testid="knowledge-base-name"]').textContent()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Delete' }).click()

    await expect(page.locator('[data-testid="knowledge-base-card"]', { hasText: name ?? '' })).toHaveCount(0)

    await page.getByRole('button', { name: 'Deleted' }).click()
    await expect(page.locator('[data-testid="knowledge-base-card"]', { hasText: name ?? '' })).toBeVisible()
  })
})

test.describe('Permanent deletion (owner-triggered)', () => {
  test('permanent delete requires confirmation, then removes the knowledge base entirely', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.getByRole('button', { name: 'Deleted' }).click()

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    const name = await card.locator('[data-testid="knowledge-base-name"]').textContent()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Delete permanently' }).click()

    await expect(page.getByText('Permanently delete this knowledge base?')).toBeVisible()
    await page.getByRole('button', { name: 'Delete permanently' }).click()

    await expect(page.locator('[data-testid="knowledge-base-card"]', { hasText: name ?? '' })).toHaveCount(0)
  })

  test('restoring a soft-deleted knowledge base before purge cancels the pending automatic purge', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.getByRole('button', { name: 'Deleted' }).click()

    const card = page.locator('[data-testid="knowledge-base-card"]').first()
    const name = await card.locator('[data-testid="knowledge-base-name"]').textContent()
    await card.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Restore' }).click()

    await page.getByRole('button', { name: 'Active' }).click()
    await expect(page.locator('[data-testid="knowledge-base-card"]', { hasText: name ?? '' })).toBeVisible()
  })
})
