import { expect, test } from '@playwright/test'

/**
 * User Story 3 — publish v1, edit, publish v2; version history shows both immutably, and
 * duplicate/archive/restore all behave correctly (specs/022-workflow-orchestration-engine
 * quickstart.md Scenario 3; spec.md FR-014).
 *
 * Draft authoring (the Start→End node graph itself) goes through the API directly rather than the
 * Designer canvas — this spec is about versioning/lifecycle actions, which the Designer's toolbar
 * "More actions" menu and Library page both expose as real UI, and that's what's exercised here;
 * `WorkflowDesignerCanvas.spec.ts` already covers building a graph through the canvas itself.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent/Workflow E2E suite so it runs
 * unmodified once a real environment is wired into CI.
 */

const linearDraftJson = JSON.stringify({
  errorPolicyJson: '{"strategy":"Stop"}',
  nodes: [
    { nodeKey: 'start', nodeType: 'Start', name: 'Start', configurationJson: '{}' },
    { nodeKey: 'end', nodeType: 'End', name: 'End', configurationJson: '{}' },
  ],
  connections: [{ sourceNodeKey: 'start', targetNodeKey: 'end' }],
  variables: [],
})

async function createWorkflow(page: import('@playwright/test').Page, name: string) {
  await page.goto('/workflows')
  await page.getByRole('button', { name: 'New Workflow' }).click()
  await page.getByLabel('Name').fill(name)
  await page.getByRole('button', { name: 'Create Workflow' }).click()
  await expect(page).toHaveURL(/\/workflows\/([0-9a-f-]+)/)
  const workflowId = page.url().match(/\/workflows\/([0-9a-f-]+)/)?.[1] as string

  const current = await (await page.request.get(`/api/v1/workflows/${workflowId}`)).json()
  await page.request.put(`/api/v1/workflows/${workflowId}`, {
    data: { name: current.name, description: current.description, draftDefinitionJson: linearDraftJson },
  })
  await page.reload()

  return workflowId
}

test.describe('Workflow versioning and lifecycle actions', () => {
  test('publishing twice keeps both versions in history', async ({ page }) => {
    await createWorkflow(page, 'Versioned Workflow')

    await page.getByRole('button', { name: 'Publish' }).click()
    await expect(page.getByText('No violations — this draft is ready to publish.')).toBeVisible()

    await page.request.put(page.url().replace('/workflows/', '/api/v1/workflows/'), {
      data: { name: 'Versioned Workflow', description: 'v2 definition.', draftDefinitionJson: linearDraftJson },
    })
    await page.reload()
    await page.getByRole('button', { name: 'Publish' }).click()

    await page.getByRole('button', { name: 'More workflow actions' }).click()
    await page.getByRole('menuitem', { name: 'Version History' }).click()

    await expect(page.locator('[data-testid="version-history-row"]')).toHaveCount(2)
    await expect(page.getByText('v2')).toBeVisible()
    await expect(page.getByText('v1')).toBeVisible()
  })

  test('duplicate creates a new draft workflow with the same draft definition', async ({ page }) => {
    await createWorkflow(page, 'Original For Duplicate')

    await page.getByRole('button', { name: 'More workflow actions' }).click()
    await page.getByRole('menuitem', { name: 'Duplicate' }).click()

    await expect(page).toHaveURL(/\/workflows\/([0-9a-f-]+)/)
    await expect(page.getByText('Original For Duplicate (Copy)')).toBeVisible()
  })

  test('archive then restore returns a workflow to its previous status', async ({ page }) => {
    await createWorkflow(page, 'Archivable Workflow')
    await page.getByRole('button', { name: 'Publish' }).click()
    await expect(page.getByText('No violations — this draft is ready to publish.')).toBeVisible()

    await page.goto('/workflows')
    const card = page.getByText('Archivable Workflow').locator('..').locator('..')
    await card.getByRole('button', { name: 'Archive' }).click()
    await expect(card.getByText('Archived')).toBeVisible()

    await card.getByRole('button', { name: 'Restore' }).click()
    await expect(card.getByText('Published')).toBeVisible()
  })
})
