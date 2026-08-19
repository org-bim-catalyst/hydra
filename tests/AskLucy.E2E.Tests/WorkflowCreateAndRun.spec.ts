import { expect, test } from '@playwright/test'

/**
 * User Story 1 — create a workflow, publish an immutable version, run it, and confirm the final
 * output is persisted and retrievable afterward (specs/022-workflow-orchestration-engine
 * quickstart.md Scenario 1; spec.md FR-051).
 *
 * Draft authoring via drag/drop, the Execution Monitor, and Publish-button UI are User Story
 * 2/3/6 scope — the Designer canvas that ships with User Story 2 (`WorkflowDesignerCanvas.spec.ts`)
 * covers the visual editing path. This spec sticks to User Story 1's own scope: workflow creation
 * goes through the Library page's dialog (which now navigates into the Designer route on success),
 * while draft authoring, validation, publishing, and execution go through the API directly
 * (`workflows-api.md`), matching the same convention `AgentToolApproval.spec.ts` uses for UI
 * surfaces outside a given story's scope.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent E2E suite so it runs unmodified
 * once a real environment is wired into CI.
 */

const draftDefinitionJson = JSON.stringify({
  nodes: [
    { nodeKey: 'start', nodeType: 'Start', name: 'Start', configurationJson: '{}' },
    {
      nodeKey: 'uppercase',
      nodeType: 'Transform',
      name: 'Uppercase',
      configurationJson: JSON.stringify({ expression: '{{workflow.text}}', outputField: 'result' }),
    },
    {
      nodeKey: 'end',
      nodeType: 'End',
      name: 'End',
      configurationJson: JSON.stringify({ outputs: { result: '{{steps.uppercase.result}}' } }),
    },
  ],
  connections: [
    { sourceNodeKey: 'start', targetNodeKey: 'uppercase' },
    { sourceNodeKey: 'uppercase', targetNodeKey: 'end' },
  ],
  variables: [],
})

test.describe('Workflow create and run', () => {
  test('creating a workflow, publishing it, running it, and reloading shows the same persisted result', async ({ page }) => {
    await page.goto('/workflows')

    await page.getByRole('button', { name: 'New Workflow' }).click()
    await page.getByLabel('Name').fill('Echo Workflow')
    await page.getByLabel('Description').fill('Uppercases the input text.')

    await page.getByRole('button', { name: 'Create Workflow' }).click()

    await expect(page).toHaveURL(/\/workflows\/([0-9a-f-]+)/)
    const workflowId = page.url().match(/\/workflows\/([0-9a-f-]+)/)?.[1]

    const current = await (await page.request.get(`/api/v1/workflows/${workflowId}`)).json()
    await page.request.put(`/api/v1/workflows/${workflowId}`, {
      data: { name: current.name, description: current.description, draftDefinitionJson },
    })

    const validation = await (await page.request.post(`/api/v1/workflows/${workflowId}/actions/validate`)).json()
    expect(validation).toEqual([])

    const version = await (
      await page.request.post(`/api/v1/workflows/${workflowId}/versions`, { data: { changeDescription: 'Initial version' } })
    ).json()
    expect(version.versionNumber).toBe(1)

    const started = await (
      await page.request.post('/api/v1/workflow-executions', {
        data: { workflowId, inputsJson: JSON.stringify({ text: 'hello' }) },
      })
    ).json()

    await expect
      .poll(
        async () => (await (await page.request.get(`/api/v1/workflow-executions/${started.id}`)).json()).status,
        { timeout: 30_000 },
      )
      .toBe('Completed')

    const execution = await (await page.request.get(`/api/v1/workflow-executions/${started.id}`)).json()
    expect(JSON.parse(execution.finalOutputJson).result).toBe('HELLO')
    expect(execution.nodes).toHaveLength(3)
    expect(execution.nodes.every((n: { status: string }) => n.status === 'Completed')).toBe(true)

    await page.reload()
    await expect(page.getByText('Echo Workflow')).toBeVisible()
  })
})
