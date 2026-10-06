import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { WorkflowApproval, WorkflowExecutionDetail } from '../api/workflowExecutionsApi'
import { WorkflowExecutionPage } from './WorkflowExecutionPage'

// The live-push transport isn't what is being tested; see ExecutionMonitor.a11y.test.tsx.
vi.mock('@microsoft/signalr', () => ({
  LogLevel: { Warning: 2 },
  HubConnectionBuilder: class {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return {
        on: () => {},
        onreconnected: () => {},
        onreconnecting: () => {},
        onclose: () => {},
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
      }
    }
  },
}))

const approval = (id: string, decision: WorkflowApproval['decision']): WorkflowApproval => ({
  id,
  workflowExecutionNodeId: 'node-1',
  intendedActionDescription: `Proceed past ${id}`,
  parametersJson: '{}',
  decision,
  wasPolicyBased: false,
  decidedByUserId: decision === 'Pending' ? null : 'user-1',
  decidedAtUtc: decision === 'Pending' ? null : new Date().toISOString(),
})

const execution = (approvals: WorkflowApproval[]): WorkflowExecutionDetail => ({
  id: 'exec-1',
  workflowId: 'wf-1',
  workflowVersionId: 'version-1',
  status: approvals.some((a) => a.decision === 'Pending') ? 'WaitingForApproval' : 'Completed',
  triggerType: 'Manual',
  inputsJson: '{}',
  finalOutputJson: null,
  startedAtUtc: new Date().toISOString(),
  completedAtUtc: null,
  terminationReason: null,
  nodes: [],
  approvals,
  errors: [],
  inputTokenCount: null,
  outputTokenCount: null,
  estimatedCost: null,
  createdAtUtc: new Date().toISOString(),
})

const server = setupServer(http.get('*/api/v1/workflows/wf-1/versions', () => HttpResponse.json([])))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderAt(approvals: WorkflowApproval[], query: string) {
  server.use(http.get('*/api/v1/workflow-executions/exec-1', () => HttpResponse.json(execution(approvals))))
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/workflows/wf-1/executions/exec-1${query}`]}>
        <Routes>
          <Route path="/workflows/:workflowId/executions/:executionId" element={<WorkflowExecutionPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('WorkflowExecutionPage ?approval= deep link (T145, specs/067 US5)', () => {
  it('opens the approval dialog for the pending approval the link names', async () => {
    renderAt([approval('appr-1', 'Pending')], '?approval=appr-1')

    expect(await screen.findByText('Proceed past appr-1')).toBeInTheDocument()
    expect(screen.queryByText(/This approval/)).not.toBeInTheDocument()
  })

  it('opens the one the link names when several are pending', async () => {
    renderAt([approval('appr-1', 'Pending'), approval('appr-2', 'Pending')], '?approval=appr-2')

    expect(await screen.findByText('Proceed past appr-2')).toBeInTheDocument()
    expect(screen.queryByText('Proceed past appr-1')).not.toBeInTheDocument()
  })

  it('says the approval was already decided, instead of opening a dialog', async () => {
    renderAt([approval('appr-1', 'Approve')], '?approval=appr-1')

    expect(await screen.findByText('This approval has already been decided.')).toBeInTheDocument()
    expect(screen.queryByText('Proceed past appr-1')).not.toBeInTheDocument()
  })

  it('says so when the approval is not on this execution', async () => {
    renderAt([approval('appr-1', 'Pending')], '?approval=someone-elses')

    expect(await screen.findByText('This approval could not be found.')).toBeInTheDocument()
  })
})
