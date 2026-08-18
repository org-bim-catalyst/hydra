import { expect, test } from '@playwright/test'

/**
 * User Story 5 — User groups related work into a Project so memory stays scoped
 * (specs/018-ai-memory-system quickstart.md Scenario 5, spec.md US5 AC1–AC3).
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT: requires a running backend/frontend deployment and an
 * authenticated session, mirroring every other spec in this directory's caveat. Run via
 * `npm test` from this directory against a real deployment (`E2E_BASE_URL` env var).
 */

const PROJECT_NAME = process.env.E2E_PROJECT_NAME ?? 'E2E Test Project'
const PROJECT_FACT = process.env.E2E_PROJECT_FACT ?? 'This project uses a custom teal color palette.'
const PROJECT_RECALL_QUESTION = process.env.E2E_PROJECT_RECALL_QUESTION ?? 'What color palette does this project use?'

test.describe('User groups related work into a Project so memory stays scoped', () => {
  test('a project-scoped fact is used inside the Project but not outside it', async ({ page }) => {
    // Create the Project.
    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Projects' }).click()
    await page.getByPlaceholder('New project name').fill(PROJECT_NAME)
    await page.getByRole('button', { name: 'Create' }).click()
    await expect(page.getByText(PROJECT_NAME)).toBeVisible()

    // Assign a new conversation to the Project and state the fact.
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()
    await page.getByLabel('Project').click()
    await page.getByRole('option', { name: PROJECT_NAME }).click()
    await page.getByPlaceholder('Message Ask Lucy...').fill(PROJECT_FACT)
    await page.keyboard.press('Enter')
    await expect(page.getByText(PROJECT_FACT)).toBeVisible({ timeout: 15_000 })

    // A second conversation in the same Project should recall it.
    await expect(async () => {
      await page.goto('/chat')
      await page.getByRole('button', { name: 'New chat' }).click()
      await page.getByLabel('Project').click()
      await page.getByRole('option', { name: PROJECT_NAME }).click()
      await page.getByPlaceholder('Message Ask Lucy...').fill(PROJECT_RECALL_QUESTION)
      await page.keyboard.press('Enter')
      await expect(page.locator('text=teal').first()).toBeVisible({ timeout: 15_000 })
    }).toPass({ timeout: 60_000 })

    // A conversation with no Project (general scope) must not see it.
    await page.goto('/chat')
    await page.getByRole('button', { name: 'New chat' }).click()
    await page.getByPlaceholder('Message Ask Lucy...').fill(PROJECT_RECALL_QUESTION)
    await page.keyboard.press('Enter')
    await expect(page.locator('text=teal')).not.toBeVisible({ timeout: 15_000 })
  })

  test('deleting a Project archives its memories rather than deleting them', async ({ page }) => {
    await page.goto('/memory')
    await page.getByRole('tab', { name: 'Projects' }).click()

    const projectRow = page.getByText(PROJECT_NAME).locator('..')
    await projectRow.getByRole('button', { name: 'Delete project' }).click()
    await page.getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(page.getByText(PROJECT_NAME)).not.toBeVisible()

    // The memory itself remains visible in the Memory Center, now Archived.
    await page.getByRole('tab', { name: 'All memories' }).click()
    await page.getByLabel('State').click()
    await page.getByRole('option', { name: 'Archived' }).click()
    await expect(page.getByText(PROJECT_FACT)).toBeVisible()
  })
})
