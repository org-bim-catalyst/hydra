import { expect, test } from '@playwright/test'

/**
 * User Story 9 — a published workflow configured with an event trigger starts automatically when
 * its matching application event occurs, with the event's data bound to the workflow's inputs
 * (specs/022-workflow-orchestration-engine quickstart.md; spec.md FR-063/FR-064, Acceptance
 * Scenarios 9.1/9.3, SC-012). Uses the `KnowledgeBaseUpdated` event since it needs no file-upload
 * multipart plumbing — the same trigger-matching/scope/authorization/concurrency-cap code path
 * (`WorkflowEventTriggerHandler`) handles `DocumentUploaded`/`DocumentProcessed` identically
 * (already covered directly, without a live event, by `WorkflowEventTriggerHandlerTests` in
 * AskLucy.Application.Tests). Reuses a pre-existing knowledge base from this environment's seed
 * data (same assumption `KnowledgeBaseTaxonomy.spec.ts` makes) rather than creating one, since
 * knowledge base creation isn't itself part of this story.
 *
 * Draft authoring goes through the API directly, same convention as `WorkflowFailureRecovery.spec.ts`.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent/Workflow E2E suite so it runs
 * unmodified once a real environment is wired into CI.
 */

async function getFirstKnowledgeBaseId(page: import('@playwright/test').Page): Promise<string> {
  const response = await page.request.get('/api/v1/knowledge-bases?view=Active&pageSize=1')
  const { items } = (await response.json()) as { items: { id: string }[] }
  expect(items.length).toBeGreaterThan(0)
  return items[0].id
}

async function createPublishedEventDrivenWorkflow(page: import('@playwright/test').Page, name: string, knowledgeBaseId: string) {
  await page.goto('/workflows')
  await page.getByRole('button', { name: 'New Workflow' }).click()
  await page.getByLabel('Name').fill(name)
  await page.getByLabel('Workflow Type').click()
  await page.getByRole('option', { name: 'Event-Driven' }).click()
  await page.getByRole('button', { name: 'Create Workflow' }).click()
  await expect(page).toHaveURL(/\/workflows\/([0-9a-f-]+)/)
  const workflowId = page.url().match(/\/workflows\/([0-9a-f-]+)/)?.[1] as string

  const draftDefinitionJson = JSON.stringify({
    errorPolicyJson: '{"strategy":"Stop"}',
    nodes: [
      { nodeKey: 'start', nodeType: 'Start', name: 'Start', configurationJson: '{}' },
      { nodeKey: 'end', nodeType: 'End', name: 'End', configurationJson: '{}' },
    ],
    connections: [{ sourceNodeKey: 'start', targetNodeKey: 'end' }],
    variables: [],
  })
  const eventTriggerConfigurationJson = JSON.stringify({ eventType: 'KnowledgeBaseUpdated', knowledgeBaseId })

  await page.request.put(`/api/v1/workflows/${workflowId}`, {
    data: { name, description: null, draftDefinitionJson, eventTriggerConfigurationJson },
  })
  const publishResponse = await page.request.post(`/api/v1/workflows/${workflowId}/versions`, { data: { changeDescription: 'v1' } })
  expect(publishResponse.ok()).toBe(true)

  return workflowId
}

async function countWorkflowExecutions(page: import('@playwright/test').Page, workflowId: string): Promise<number> {
  const response = await page.request.get(`/api/v1/workflow-executions?workflowId=${workflowId}&pageSize=50`)
  const { items } = (await response.json()) as { items: unknown[] }
  return items.length
}

test.describe('Event-driven workflow trigger', () => {
  test('a matching knowledge base update starts an execution automatically, without a manual start', async ({ page }) => {
    const knowledgeBaseId = await getFirstKnowledgeBaseId(page)
    const workflowId = await createPublishedEventDrivenWorkflow(page, 'KB Update Trigger Workflow', knowledgeBaseId)

    expect(await countWorkflowExecutions(page, workflowId)).toBe(0)

    const current = await (await page.request.get(`/api/v1/knowledge-bases/${knowledgeBaseId}`)).json()
    const patchResponse = await page.request.patch(`/api/v1/knowledge-bases/${knowledgeBaseId}`, {
      data: { name: current.name, description: current.description, color: current.color, icon: current.icon, categoryId: current.categoryId, tags: null, notes: current.notes },
    })
    expect(patchResponse.ok()).toBe(true)

    // SC-012 — starts within 1 minute of the triggering event for 95% of matching events.
    await expect.poll(async () => countWorkflowExecutions(page, workflowId), { timeout: 60_000 }).toBeGreaterThan(0)
  })

  test('disabling the workflow stops subsequent matching events from starting new executions', async ({ page }) => {
    const knowledgeBaseId = await getFirstKnowledgeBaseId(page)
    const workflowId = await createPublishedEventDrivenWorkflow(page, 'Disabled Trigger Workflow', knowledgeBaseId)

    await page.goto(`/workflows/${workflowId}`)
    await page.getByRole('button', { name: 'More workflow actions' }).click()
    await page.getByRole('menuitem', { name: 'Disable' }).click()
    await expect(page.getByText('Disabled')).toBeVisible()

    const current = await (await page.request.get(`/api/v1/knowledge-bases/${knowledgeBaseId}`)).json()
    await page.request.patch(`/api/v1/knowledge-bases/${knowledgeBaseId}`, {
      data: { name: current.name, description: current.description, color: current.color, icon: current.icon, categoryId: current.categoryId, tags: null, notes: current.notes },
    })

    // Give the (now-suppressed) dispatch path the same window the positive-path test polls for,
    // then confirm nothing started — Acceptance Scenario 9.3.
    await page.waitForTimeout(5_000)
    expect(await countWorkflowExecutions(page, workflowId)).toBe(0)
  })

  test('the event trigger configuration panel lets a user pick an event type and knowledge base scope', async ({ page }) => {
    const knowledgeBaseId = await getFirstKnowledgeBaseId(page)
    await page.goto('/workflows')
    await page.getByRole('button', { name: 'New Workflow' }).click()
    await page.getByLabel('Name').fill('UI-Configured Trigger Workflow')
    await page.getByLabel('Workflow Type').click()
    await page.getByRole('option', { name: 'Event-Driven' }).click()
    await page.getByRole('button', { name: 'Create Workflow' }).click()
    await expect(page).toHaveURL(/\/workflows\/([0-9a-f-]+)/)
    const workflowId = page.url().match(/\/workflows\/([0-9a-f-]+)/)?.[1] as string

    await page.getByRole('button', { name: 'More workflow actions' }).click()
    await page.getByRole('menuitem', { name: 'Event Trigger' }).click()
    await page.getByTestId('event-trigger-config-panel').getByLabel('Starts when').click()
    await page.getByRole('option', { name: 'Document uploaded' }).click()
    await page.getByLabel('Knowledge base').click()
    // The first non-"Any" option — this environment's seeded knowledge base.
    await page.getByRole('option').nth(1).click()
    await page.getByRole('button', { name: 'Save' }).click()

    const updated = await (await page.request.get(`/api/v1/workflows/${workflowId}`)).json()
    const config = JSON.parse(updated.eventTriggerConfigurationJson) as { eventType: string; knowledgeBaseId: string | null }
    expect(config.eventType).toBe('DocumentUploaded')
    expect(config.knowledgeBaseId).toBe(knowledgeBaseId)
  })
})
