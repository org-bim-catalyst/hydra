import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AgentApproval, AgentExecutionDetail } from '../api/agentExecutionsApi'
import { AgentExecutionPage } from './AgentExecutionPage'

const approval = (id: string, decision: AgentApproval['decision']): AgentApproval => ({
  id,
  agentToolCallId: null,
  intendedActionDescription: `Execute tool ${id}`,
  intendedParametersJson: '{}',
  decision,
  decidedByUserId: decision === 'Pending' ? null : 'user-1',
  wasPolicyBased: false,
  decidedAtUtc: decision === 'Pending' ? null : new Date().toISOString(),
})

const execution = (approvals: AgentApproval[]): AgentExecutionDetail => ({
  id: 'exec-1',
  agentId: 'agent-1',
  agentVersionId: 'version-1',
  agentVersionNumber: 1,
  objective: 'Do the risky thing.',
  status: approvals.some((a) => a.decision === 'Pending') ? 'WaitingForApproval' : 'Completed',
  isTestExecution: false,
  conversationIntegrationMode: 'Standalone',
  userChatId: null,
  finalOutputText: null,
  finalOutputJson: null,
  startedAtUtc: new Date().toISOString(),
  completedAtUtc: null,
  terminationReason: null,
  steps: [],
  approvals,
  errors: [],
  inputTokenCount: null,
  outputTokenCount: null,
  estimatedCost: null,
  createdAtUtc: new Date().toISOString(),
})

const server = setupServer(
  http.get('*/api/v1/agent-executions/exec-1/tool-calls', () => HttpResponse.json([])),
  http.get('*/api/v1/agent-executions/exec-1/usage', () =>
    HttpResponse.json({ inputTokenCount: 0, outputTokenCount: 0, stepCount: 0, toolCallCount: 0, estimatedCost: null, costCurrency: 'USD' }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderAt(approvals: AgentApproval[], query: string) {
  server.use(http.get('*/api/v1/agent-executions/exec-1', () => HttpResponse.json(execution(approvals))))
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/agents/agent-1/executions/exec-1${query}`]}>
        <Routes>
          <Route path="/agents/:agentId/executions/:executionId" element={<AgentExecutionPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AgentExecutionPage ?approval= deep link (T145, specs/067 US5)', () => {
  it('opens the approval dialog for the pending approval the link names', async () => {
    renderAt([approval('appr-1', 'Pending')], '?approval=appr-1')

    expect(await screen.findByText('This agent wants to take an action')).toBeInTheDocument()
    expect(screen.getAllByText('Execute tool appr-1').length).toBeGreaterThan(0)
  })

  it('opens the one the link names when several are pending', async () => {
    renderAt([approval('appr-1', 'Pending'), approval('appr-2', 'Pending')], '?approval=appr-2')

    expect(await screen.findByText('This agent wants to take an action')).toBeInTheDocument()
    expect(screen.getByTestId('approval-parameters')).toBeInTheDocument()
    expect(screen.getAllByText('Execute tool appr-2').length).toBeGreaterThan(1)
  })

  it('says the approval was already decided, instead of opening a dialog', async () => {
    renderAt([approval('appr-1', 'Approved')], '?approval=appr-1')

    expect(await screen.findByText('This approval has already been decided.')).toBeInTheDocument()
    expect(screen.queryByText('This agent wants to take an action')).not.toBeInTheDocument()
  })

  it('says so when the approval is not on this execution', async () => {
    renderAt([approval('appr-1', 'Pending')], '?approval=someone-elses')

    expect(await screen.findByText('This approval could not be found.')).toBeInTheDocument()
    expect(screen.queryByText('This agent wants to take an action')).not.toBeInTheDocument()
  })

  it('shows no dialog and no message when the page is opened without the parameter', async () => {
    renderAt([approval('appr-1', 'Pending')], '')

    expect(await screen.findByText('Execution history')).toBeInTheDocument()
    expect(screen.queryByText('This agent wants to take an action')).not.toBeInTheDocument()
    expect(screen.queryByText(/This approval/)).not.toBeInTheDocument()
  })
})
