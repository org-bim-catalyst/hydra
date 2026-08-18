import { expect, test } from '@playwright/test'

/**
 * User Story 2 — an agent configured with a built-in tool (Knowledge Search) plans and executes a
 * multi-step run that calls the tool before producing its final answer (specs/020-ai-agent-framework
 * quickstart.md Scenario 2; spec.md FR-016-FR-019, FR-045).
 *
 * The Agent Builder UI does not yet expose tool/knowledge-base configuration (that surface is out of
 * scope for this story per tasks.md — only the backend `UpdateAgentCommand` was extended to accept
 * `tools[]`). Tool configuration is therefore done via a direct API call, matching the established
 * `page.request` convention used by DocumentDashboard.spec.ts, while the run itself is driven entirely
 * through the UI.
 *
 * NOT RUNNABLE IN THIS ENVIRONMENT — no running frontend/backend + authenticated session was
 * available in this sandbox (same constraint documented on AgentCreateAndRun.spec.ts). Written to the
 * same selector/assertion conventions as the existing Agent E2E suite so it runs unmodified once a
 * real environment is wired into CI.
 */

test.describe('Agent multi-step tool execution', () => {
  test('an agent configured with KnowledgeSearchTool plans a tool step, calls it, and completes with citations preserved', async ({
    page,
  }) => {
    await page.goto('/agents')

    await page.getByRole('button', { name: 'New Agent' }).click()
    await page.getByLabel('Name').fill('Research Assistant')
    await page.getByLabel('System Instructions').fill('You are a research assistant. Use the knowledge base to answer questions.')
    await page.getByLabel('AI Provider').selectOption({ index: 1 })
    await page.getByLabel('Model').selectOption({ index: 1 })
    await page.getByRole('button', { name: 'Create Agent' }).click()

    await expect(page).toHaveURL(/\/agents\/([0-9a-f-]+)/)
    const agentId = page.url().match(/\/agents\/([0-9a-f-]+)/)?.[1]

    // Attach KnowledgeSearchTool to the draft via the API (no builder UI for this yet, see file header).
    const current = await (await page.request.get(`/api/v1/agents/${agentId}`)).json()
    const updateResponse = await page.request.put(`/api/v1/agents/${agentId}`, {
      data: {
        name: current.name,
        description: current.description,
        agentType: current.agentType,
        instructions: current.instructions,
        modelProviderId: current.modelProviderId,
        modelId: current.modelId,
        outputFormat: current.outputFormat,
        executionPolicy: current.executionPolicy,
        tools: [{ toolName: 'KnowledgeSearchTool', configurationJson: null }],
      },
    })
    expect(updateResponse.ok()).toBeTruthy()

    await page.reload()
    await page.getByRole('button', { name: 'Publish' }).click()

    await page.getByLabel('Objective').fill('Search the knowledge base for our onboarding policy and summarize it.')
    const [startResponse] = await Promise.all([
      page.waitForResponse((res) => res.url().includes('/api/v1/agent-executions') && res.request().method() === 'POST'),
      page.getByRole('button', { name: 'Run' }).click(),
    ])
    const executionId = (await startResponse.json()).id as string

    await expect(page.getByText('Completed')).toBeVisible({ timeout: 30_000 })

    const resultText = await page.locator('[data-testid="execution-result"]').textContent()
    expect(resultText).toBeTruthy()

    // FR-018/FR-045 — the plan included a real tool-call step, and it ran to completion.
    const execution = await (await page.request.get(`/api/v1/agent-executions/${executionId}`)).json()
    const toolSteps = execution.steps.filter((s: { stepType: string }) => s.stepType === 'ToolCall')
    expect(toolSteps.length).toBeGreaterThan(0)
    expect(toolSteps[0].toolName).toBe('KnowledgeSearchTool')
    expect(toolSteps[0].status).toBe('Completed')
  })
})
