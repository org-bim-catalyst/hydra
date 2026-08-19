import { expect, test } from '@playwright/test'

/**
 * User Story 4 — Condition nodes route to exactly one branch and record the skipped one; Parallel
 * branches run concurrently and converge at a Merge node per an explicit strategy; loops are
 * always bounded (specs/022-workflow-orchestration-engine quickstart.md Scenario 4; spec.md
 * FR-029–FR-032).
 *
 * Draft authoring goes through the API directly, same convention as `WorkflowCreateAndRun.spec.ts`
 * — the Designer canvas doesn't yet have dedicated UI for editing a Condition's branch labels or a
 * Merge node's `branchNodeKeys` (`WorkflowDesignerCanvas.spec.ts` covers the canvas itself).
 * Parallel branches use Transform nodes rather than RagSearch/MemorySearch, since those need a
 * seeded Knowledge Base/memory to produce real output — Transform proves the same branching/merge
 * mechanics without that prerequisite.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to
 * the same selector/assertion conventions as the existing Agent/Workflow E2E suite so it runs
 * unmodified once a real environment is wired into CI.
 */

async function createPublishedWorkflow(page: import('@playwright/test').Page, name: string, draftDefinitionJson: string) {
  await page.goto('/workflows')
  await page.getByRole('button', { name: 'New Workflow' }).click()
  await page.getByLabel('Name').fill(name)
  await page.getByRole('button', { name: 'Create Workflow' }).click()
  await expect(page).toHaveURL(/\/workflows\/([0-9a-f-]+)/)
  const workflowId = page.url().match(/\/workflows\/([0-9a-f-]+)/)?.[1] as string

  const current = await (await page.request.get(`/api/v1/workflows/${workflowId}`)).json()
  await page.request.put(`/api/v1/workflows/${workflowId}`, {
    data: { name: current.name, description: current.description, draftDefinitionJson },
  })
  const publishResponse = await page.request.post(`/api/v1/workflows/${workflowId}/versions`, { data: { changeDescription: 'v1' } })
  expect(publishResponse.ok()).toBe(true)

  return workflowId
}

async function runAndAwaitCompletion(page: import('@playwright/test').Page, workflowId: string, inputsJson: string) {
  const started = await (
    await page.request.post('/api/v1/workflow-executions', { data: { workflowId, inputsJson } })
  ).json()

  await expect
    .poll(async () => (await (await page.request.get(`/api/v1/workflow-executions/${started.id}`)).json()).status, { timeout: 30_000 })
    .toBe('Completed')

  return (await page.request.get(`/api/v1/workflow-executions/${started.id}`)).json()
}

test.describe('Workflow Condition/Parallel/Merge branching', () => {
  const draftDefinitionJson = JSON.stringify({
    errorPolicyJson: '{"strategy":"Stop"}',
    nodes: [
      { nodeKey: 'start', nodeType: 'Start', name: 'Start', configurationJson: '{}' },
      { nodeKey: 'condition', nodeType: 'Condition', name: 'Is Urgent?', configurationJson: JSON.stringify({ expression: '{{workflow.category}} == "urgent"' }) },
      { nodeKey: 'branchA', nodeType: 'Transform', name: 'Branch A', configurationJson: JSON.stringify({ expression: '"a-value"', outputField: 'value' }) },
      { nodeKey: 'branchB', nodeType: 'Transform', name: 'Branch B', configurationJson: JSON.stringify({ expression: '"b-value"', outputField: 'value' }) },
      { nodeKey: 'parallel', nodeType: 'Parallel', name: 'Fan Out', configurationJson: '{}' },
      { nodeKey: 'merge', nodeType: 'Merge', name: 'Merge', configurationJson: JSON.stringify({ strategy: 'CollectAll', branchNodeKeys: ['branchA', 'branchB'] }) },
      { nodeKey: 'end', nodeType: 'End', name: 'End', configurationJson: '{}' },
    ],
    connections: [
      { sourceNodeKey: 'start', targetNodeKey: 'condition' },
      { sourceNodeKey: 'condition', targetNodeKey: 'parallel', branchLabel: 'true' },
      { sourceNodeKey: 'condition', targetNodeKey: 'end', branchLabel: 'false' },
      { sourceNodeKey: 'parallel', targetNodeKey: 'branchA' },
      { sourceNodeKey: 'parallel', targetNodeKey: 'branchB' },
      { sourceNodeKey: 'branchA', targetNodeKey: 'merge' },
      { sourceNodeKey: 'branchB', targetNodeKey: 'merge' },
      { sourceNodeKey: 'merge', targetNodeKey: 'end' },
    ],
    variables: [],
  })

  test('an urgent input takes the true branch through Parallel/Merge; both branches complete', async ({ page }) => {
    const workflowId = await createPublishedWorkflow(page, 'Branching Workflow — Urgent', draftDefinitionJson)

    const execution = await runAndAwaitCompletion(page, workflowId, JSON.stringify({ category: 'urgent' }))

    expect(execution.status).toBe('Completed')
    expect(execution.nodes.filter((n: { status: string }) => n.status === 'Skipped')).toHaveLength(0)
    expect(execution.nodes.filter((n: { status: string }) => n.status === 'Completed').length).toBeGreaterThanOrEqual(6) // start, condition, parallel's 2 branches, merge, end
  })

  test('a routine input takes the false branch directly to End; the Parallel/Merge/branch nodes are skipped', async ({ page }) => {
    const workflowId = await createPublishedWorkflow(page, 'Branching Workflow — Routine', draftDefinitionJson)

    const execution = await runAndAwaitCompletion(page, workflowId, JSON.stringify({ category: 'routine' }))

    expect(execution.status).toBe('Completed')
    expect(execution.nodes.filter((n: { status: string }) => n.status === 'Skipped').length).toBeGreaterThanOrEqual(4) // parallel, branchA, branchB, merge
  })
})

test.describe('Workflow bounded loop', () => {
  test('a loop always stops at its declared maximum iterations, never running unbounded', async ({ page }) => {
    const draftDefinitionJson = JSON.stringify({
      errorPolicyJson: '{"strategy":"Stop"}',
      nodes: [
        { nodeKey: 'start', nodeType: 'Start', name: 'Start', configurationJson: '{}' },
        { nodeKey: 'loopBody', nodeType: 'Transform', name: 'Loop Body', configurationJson: JSON.stringify({ maxIterations: 3, expression: '{{workflow.text}}', outputField: 'result' }) },
        { nodeKey: 'end', nodeType: 'End', name: 'End', configurationJson: '{}' },
      ],
      connections: [
        { sourceNodeKey: 'start', targetNodeKey: 'loopBody' },
        { sourceNodeKey: 'loopBody', targetNodeKey: 'loopBody', branchLabel: 'loop-back' },
        { sourceNodeKey: 'loopBody', targetNodeKey: 'end' },
      ],
      variables: [],
    })

    const workflowId = await createPublishedWorkflow(page, 'Bounded Loop Workflow', draftDefinitionJson)
    const execution = await runAndAwaitCompletion(page, workflowId, JSON.stringify({ text: 'hello' }))

    expect(execution.status).toBe('Completed')
    const loopBodyRows = execution.nodes.filter((n: { outputJson: string | null }) => n.outputJson?.includes('hello'))
    expect(loopBodyRows).toHaveLength(3)
  })
})
