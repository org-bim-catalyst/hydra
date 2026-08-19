import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { WorkflowDesignerPage } from './WorkflowDesignerPage'

expect.extend(toHaveNoViolations)

// @xyflow/react (embedded via WorkflowCanvas) measures each node/the viewport via
// ResizeObserver, which jsdom doesn't implement — an observer that never fires leaves React Flow
// waiting forever, which manifests as an infinite re-render loop. Firing the callback once,
// synchronously, with the target's (zero-sized, under jsdom) rect lets it settle. Also needs
// DOMMatrixReadOnly (used to read the canvas transform's scale) — @xyflow/react's own documented
// jsdom testing workaround.
class ResizeObserverStub {
  private readonly callback: ResizeObserverCallback

  constructor(callback: ResizeObserverCallback) {
    this.callback = callback
  }

  observe(target: Element) {
    this.callback([{ target, contentRect: target.getBoundingClientRect() } as ResizeObserverEntry], this as unknown as ResizeObserver)
  }

  unobserve() {}
  disconnect() {}
}

class DOMMatrixReadOnlyStub {
  m22 = 1

  constructor(transform: string) {
    const scale = transform?.match(/scale\(([1-9.]+)\)/)?.[1]
    this.m22 = scale !== undefined ? Number(scale) : 1
  }
}

const server = setupServer(
  http.get('*/api/v1/workflows/workflow-1', () =>
    HttpResponse.json({
      id: 'workflow-1',
      name: 'Test Workflow',
      description: null,
      workflowType: 'Manual',
      status: 'Draft',
      draftDefinitionJson: '{}',
      publishedVersionNumber: null,
      eventTriggerConfigurationJson: null,
      createdAtUtc: new Date().toISOString(),
      modifiedAtUtc: null,
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
beforeEach(() => {
  vi.stubGlobal('ResizeObserver', ResizeObserverStub)
  vi.stubGlobal('DOMMatrixReadOnly', DOMMatrixReadOnlyStub)
})
afterEach(() => {
  server.resetHandlers()
  vi.unstubAllGlobals()
})
afterAll(() => server.close())

describe('WorkflowDesignerPage accessibility (spec.md User Story 2)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { findByRole, container } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/workflows/workflow-1']}>
          <Routes>
            <Route path="/workflows/:id" element={<WorkflowDesignerPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByRole('heading', { name: 'Test Workflow' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
