import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { ExecutionMonitor } from './ExecutionMonitor'

expect.extend(toHaveNoViolations)

// useWorkflowExecutionHub opens a real SignalR connection, which jsdom can't resolve a hub URL
// for outside a real browser navigation context. This test is about the initially-rendered DOM's
// accessibility, not live-push behavior, so the transport itself is stubbed out — `isLive` simply
// stays false, which the component already renders as a visible "Reconnecting…" state.
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

const server = setupServer(
  http.get('*/api/v1/workflow-executions/execution-1', () =>
    HttpResponse.json({
      id: 'execution-1',
      workflowId: 'workflow-1',
      workflowVersionId: 'version-1',
      status: 'Running',
      triggerType: 'Manual',
      inputsJson: '{}',
      finalOutputJson: null,
      startedAtUtc: new Date().toISOString(),
      completedAtUtc: null,
      terminationReason: null,
      nodes: [
        { id: 'node-1', workflowNodeId: 'wn-1', status: 'Completed', outputJson: null, retryCount: 0, skippedReason: null, startedAtUtc: null, completedAtUtc: null },
      ],
      approvals: [],
      errors: [],
      inputTokenCount: null,
      outputTokenCount: null,
      estimatedCost: null,
      createdAtUtc: new Date().toISOString(),
    }),
  ),
  http.get('*/api/v1/workflows/workflow-1/versions', () => HttpResponse.json([])),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('ExecutionMonitor accessibility (spec.md User Story 6)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { findByText, container } = render(
      <QueryClientProvider client={queryClient}>
        <ExecutionMonitor executionId="execution-1" workflowId="workflow-1" />
      </QueryClientProvider>,
    )

    await findByText('Running')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
