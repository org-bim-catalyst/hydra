import { expect, test } from '@playwright/test'
import path from 'node:path'

/**
 * User Story 2 — folder organization and document upload (specs/014-knowledge-base-management
 * quickstart.md Scenario 2).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — same documented constraint as KnowledgeBaseLifecycle.spec.ts.
 */

test.describe('Knowledge base folders and documents', () => {
  test('creating a folder and a nested subfolder reflects in the tree view', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.locator('[data-testid="knowledge-base-card"]').first().click()

    await page.getByRole('button', { name: 'New Folder' }).click()
    await page.getByLabel('Folder name').fill('2026 Contracts')
    await page.getByRole('button', { name: 'Create' }).click()
    await expect(page.getByRole('treeitem', { name: '2026 Contracts' })).toBeVisible()

    await page.getByRole('treeitem', { name: '2026 Contracts' }).getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'New Subfolder' }).click()
    await page.getByLabel('Folder name').fill('Client A')
    await page.getByRole('button', { name: 'Create' }).click()

    await page.getByRole('treeitem', { name: '2026 Contracts' }).getByRole('button', { name: 'Expand' }).click()
    await expect(page.getByRole('treeitem', { name: 'Client A' })).toBeVisible()
  })

  test('uploading a supported document succeeds and shows page count', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.locator('[data-testid="knowledge-base-card"]').first().click()

    const fileInput = page.locator('input[type="file"]')
    await fileInput.setInputFiles(path.join(__dirname, 'fixtures', 'sample.pdf'))

    const documentRow = page.locator('[data-testid="knowledge-base-document"]', { hasText: 'sample.pdf' })
    await expect(documentRow).toBeVisible()
    await expect(documentRow.locator('[data-testid="document-page-count"]')).not.toBeEmpty()
  })

  test('uploading a mislabeled file is rejected with a specific message', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.locator('[data-testid="knowledge-base-card"]').first().click()

    const fileInput = page.locator('input[type="file"]')
    await fileInput.setInputFiles(path.join(__dirname, 'fixtures', 'renamed-text-file.pdf'))

    await expect(page.getByText(/does not match any supported document type|but its name has extension/)).toBeVisible()
  })

  test('dragging a document into a different folder moves it, without a page reload', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.locator('[data-testid="knowledge-base-card"]').first().click()

    const document = page.locator('[data-testid="knowledge-base-document"]').first()
    const targetFolder = page.getByRole('treeitem', { name: '2026 Contracts' })
    await document.dragTo(targetFolder)

    await targetFolder.getByRole('button', { name: 'Expand' }).click()
    await expect(targetFolder.locator('[data-testid="knowledge-base-document"]')).toHaveCount(1)
  })

  test('moving a document via keyboard (no pointer input) reaches the same result as drag-and-drop', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.locator('[data-testid="knowledge-base-card"]').first().click()

    const document = page.locator('[data-testid="knowledge-base-document"]').first()
    await document.focus()
    await page.keyboard.press('Enter') // opens the "Move to..." keyboard-accessible equivalent (FR-040)
    await page.getByRole('option', { name: '2026 Contracts' }).click()

    await expect(page.getByText('Moved to 2026 Contracts')).toBeVisible()
  })

  test('nesting a folder past the configured depth limit is blocked with an explanation', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.locator('[data-testid="knowledge-base-card"]').first().click()

    // Assumes a knowledge base seeded 10 levels deep for this scenario.
    const deepestFolder = page.getByRole('treeitem').last()
    await deepestFolder.getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'New Subfolder' }).click()
    await page.getByLabel('Folder name').fill('TooDeep')
    await page.getByRole('button', { name: 'Create' }).click()

    await expect(page.getByText(/cannot be nested deeper than/)).toBeVisible()
  })

  test('moving a folder into its own descendant is rejected with an explanation', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.locator('[data-testid="knowledge-base-card"]').first().click()

    const parentFolder = page.getByRole('treeitem', { name: '2026 Contracts' })
    const childFolder = page.getByRole('treeitem', { name: 'Client A' })
    await parentFolder.dragTo(childFolder)

    await expect(page.getByText(/cannot be moved into itself or one of its own subfolders/)).toBeVisible()
  })

  test('deleting a non-empty folder requires confirmation and states what it contains', async ({ page }) => {
    await page.goto('/knowledge-bases')
    await page.locator('[data-testid="knowledge-base-card"]').first().click()

    await page.getByRole('treeitem', { name: '2026 Contracts' }).getByLabel('More actions').click()
    await page.getByRole('menuitem', { name: 'Delete' }).click()

    await expect(page.getByText(/still contains subfolders or documents/)).toBeVisible()
    await page.getByRole('button', { name: 'Delete anyway' }).click()

    await expect(page.getByRole('treeitem', { name: '2026 Contracts' })).toHaveCount(0)
  })
})
