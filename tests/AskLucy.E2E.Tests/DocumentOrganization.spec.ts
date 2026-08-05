import { expect, test } from '@playwright/test'

/**
 * User Story 4 — organize documents into folders and find them again
 * (specs/015-document-intelligence-pipeline quickstart.md Scenario 4).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on DocumentUploadLifecycle.spec.ts and
 * other existing suites in this project).
 */

test.describe('Document organization and discovery', () => {
  test('create a folder, move documents into it, duplicate one', async ({ page }) => {
    await page.goto('/documents')

    await page.getByRole('button', { name: 'New folder' }).click()
    await page.getByPlaceholder('Folder name').fill('Invoices')
    await page.getByRole('button', { name: 'Create' }).click()
    await expect(page.getByText('Invoices (0)')).toBeVisible()

    const card = page.locator('.MuiCard-root').first()
    const fileName = (await card.locator('.MuiTypography-subtitle2').textContent()) ?? ''

    await card.getByLabel(`Move ${fileName} to a folder`).click()
    await page.getByRole('menuitem', { name: 'Invoices' }).click()
    await expect(page.getByText('Invoices (1)')).toBeVisible()

    await card.getByLabel(`Duplicate ${fileName}`).click()
    await expect(page.locator('.MuiCard-root', { hasText: fileName })).toHaveCount(2)
  })

  test('combined filters return only the intersection', async ({ page }) => {
    await page.goto('/documents')

    await page.getByLabel('Search').fill('invoice')
    await page.getByLabel('Author').fill('Jane')
    await page.getByLabel('Status').click()
    await page.getByRole('option', { name: 'Completed' }).click()

    await expect(page.locator('.MuiCard-root')).toHaveCount(1)
  })

  test('deleting a non-empty folder requires an explicit choice for its documents', async ({ page }) => {
    await page.goto('/documents')

    const folderRow = page.locator('.MuiListItemButton-root', { hasText: /\(\d+\)/ }).first()
    await folderRow.getByRole('button', { name: /Delete/ }).click()

    const dialog = page.getByRole('dialog')
    await expect(dialog.getByText('What should happen to the documents in this folder?')).toBeVisible()
    await dialog.getByRole('combobox').click()
    await page.getByRole('option', { name: 'Archive all' }).click()
    await dialog.getByRole('button', { name: 'Delete' }).click()

    await expect(dialog).not.toBeVisible()
  })
})
