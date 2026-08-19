import { expect, test } from '@playwright/test'

/**
 * User Story 5 — a Human Approval node pauses execution for an explicit decision; Approve resumes
 * and completes the run, Reject/Request Changes end it with the decision's own explanation recorded
 * (specs/022-workflow-orchestration-engine quickstart.md Scenario 5; spec.md FR-033/FR-034/FR-038).
 *
 * Draft authoring and the approval decision both go through the API directly, same convention as
 * `WorkflowBranchingParallelLoop.spec.ts` — the Workflow execution monitor page that composes
 * `ApprovalDialog` doesn't exist yet (a User Story 6 deliverable, `WorkflowExecutionPage.tsx`); this
 * spec exercises the same pause/approve/reject/request-changes mechanics `ApprovalDialog` itself
 * drives, at the API layer. The platform-mandatory-baseline/policy-bypass mechanics are already
 * covered thoroughly at the Application layer (`WorkflowApprovalWorkflowTests`), matching the same
 * scope boundary `AgentToolApproval.spec.ts` draws for the Agent Runtime's own policy-bypass path.
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

const draftDefinitionJson = JSON.stringify({
  errorPolicyJson: '{"strategy":"Stop"}',
  nodes: [
    { nodeKey: 'start', nodeType: 'Start', name: 'Start', configurationJson: '{}' },
    { nodeKey: 'approval', nodeType: 'HumanApproval', name: 'Manager Sign-Off', configurationJson: '{}' },
    { nodeKey: 'end', nodeType: 'End', name: 'End', configurationJson: '{}' },
  ],
  connections: [
    { sourceNodeKey: 'start', targetNodeKey: 'approval' },
    { sourceNodeKey: 'approval', targetNodeKey: 'end' },
  ],
  variables: [],
})

async function startExecutionAndAwaitWaiting(page: import('@playwright/test').Page, workflowId: string) {
  const started = await (await page.request.post('/api/v1/workflow-executions', { data: { workflowId, inputsJson: '{}' } })).json()

  await expect
    .poll(async () => (await (await page.request.get(`/api/v1/workflow-executions/${started.id}`)).json()).status, { timeout: 30_000 })
    .toBe('WaitingForApproval')

  return (await page.request.get(`/api/v1/workflow-executions/${started.id}`)).json()
}

test.describe('Workflow Human Approval gate', () => {
  test('a Human Approval node pauses execution with the pending action visible', async ({ page }) => {
    const workflowId = await createPublishedWorkflow(page, 'Approval Workflow — Pause', draftDefinitionJson)

    const execution = await startExecutionAndAwaitWaiting(page, workflowId)

    expect(execution.status).toBe('WaitingForApproval')
    expect(execution.approvals).toHaveLength(1)
    expect(execution.approvals[0].decision).toBe('Pending')
    expect(execution.approvals[0].intendedActionDescription).toContain('approval')
  })

  test('approving the pending approval resumes and completes the execution', async ({ page }) => {
    const workflowId = await createPublishedWorkflow(page, 'Approval Workflow — Approve', draftDefinitionJson)
    const execution = await startExecutionAndAwaitWaiting(page, workflowId)

    const approveResponse = await page.request.post(
      `/api/v1/workflow-executions/${execution.id}/approvals/${execution.approvals[0].id}/approve`,
    )
    expect(approveResponse.ok()).toBe(true)

    await expect
      .poll(async () => (await (await page.request.get(`/api/v1/workflow-executions/${execution.id}`)).json()).status, { timeout: 30_000 })
      .toBe('Completed')
  })

  test('rejecting the pending approval ends the execution with the given reason, never proceeding past the gate', async ({ page }) => {
    const workflowId = await createPublishedWorkflow(page, 'Approval Workflow — Reject', draftDefinitionJson)
    const execution = await startExecutionAndAwaitWaiting(page, workflowId)

    const rejectResponse = await page.request.post(`/api/v1/workflow-executions/${execution.id}/approvals/${execution.approvals[0].id}/reject`, {
      data: { reason: 'Not authorized for this run.' },
    })
    expect(rejectResponse.ok()).toBe(true)

    const finalExecution = await (await page.request.get(`/api/v1/workflow-executions/${execution.id}`)).json()
    expect(finalExecution.status).toBe('Failed')
    expect(finalExecution.terminationReason).toContain('Not authorized for this run.')
  })

  test('requesting changes ends the execution with the decision and comments recorded', async ({ page }) => {
    const workflowId = await createPublishedWorkflow(page, 'Approval Workflow — Request Changes', draftDefinitionJson)
    const execution = await startExecutionAndAwaitWaiting(page, workflowId)

    const requestChangesResponse = await page.request.post(
      `/api/v1/workflow-executions/${execution.id}/approvals/${execution.approvals[0].id}/request-changes`,
      { data: { comments: 'Please narrow the scope before proceeding.' } },
    )
    expect(requestChangesResponse.ok()).toBe(true)

    const finalExecution = await (await page.request.get(`/api/v1/workflow-executions/${execution.id}`)).json()
    expect(finalExecution.status).toBe('Failed')
    expect(finalExecution.approvals[0].decision).toBe('RequestChanges')
    expect(finalExecution.terminationReason).toContain('Please narrow the scope before proceeding.')
  })
})
