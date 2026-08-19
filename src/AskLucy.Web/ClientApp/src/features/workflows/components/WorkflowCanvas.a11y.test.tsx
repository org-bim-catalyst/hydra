import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useWorkflowCanvasStore } from '../store/workflowCanvasStore'
import { WorkflowCanvas } from './WorkflowCanvas'

expect.extend(toHaveNoViolations)

// @xyflow/react measures each node/the viewport via ResizeObserver, which jsdom doesn't
// implement. A no-op stub leaves React Flow waiting forever for a measurement that never
// arrives, which manifests as an infinite re-render loop — firing the callback once,
// synchronously, with the target's (zero-sized, under jsdom) rect is what lets it settle.
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

// @xyflow/react's own documented jsdom testing workaround — jsdom has no DOMMatrixReadOnly, which
// it uses to read the canvas transform's scale.
class DOMMatrixReadOnlyStub {
  m22 = 1

  constructor(transform: string) {
    const scale = transform?.match(/scale\(([1-9.]+)\)/)?.[1]
    this.m22 = scale !== undefined ? Number(scale) : 1
  }
}

describe('WorkflowCanvas accessibility (spec.md User Story 2)', () => {
  beforeEach(() => {
    vi.stubGlobal('ResizeObserver', ResizeObserverStub)
    vi.stubGlobal('DOMMatrixReadOnly', DOMMatrixReadOnlyStub)
    useWorkflowCanvasStore.getState().loadDefinition({
      inputsSchemaJson: '{}',
      outputsSchemaJson: '{}',
      errorPolicyJson: '{"strategy":"Stop"}',
      executionPolicyJson: '{}',
      securityPolicyJson: '{}',
      nodes: [
        { nodeKey: 'start', nodeType: 'Start', name: 'Start', description: null, inputSchemaJson: '{}', outputSchemaJson: '{}', configurationJson: '{}', requiredPermissionsJson: '[]', timeoutSeconds: null, retryPolicyJson: null, approvalPolicy: 'NeverRequire', idempotencyKeyExpression: null, compensatingNodeKey: null, canvasX: 0, canvasY: 0 },
        { nodeKey: 'end', nodeType: 'End', name: 'End', description: null, inputSchemaJson: '{}', outputSchemaJson: '{}', configurationJson: '{}', requiredPermissionsJson: '[]', timeoutSeconds: null, retryPolicyJson: null, approvalPolicy: 'NeverRequire', idempotencyKeyExpression: null, compensatingNodeKey: null, canvasX: 0, canvasY: 200 },
      ],
      connections: [{ sourceNodeKey: 'start', targetNodeKey: 'end', branchLabel: null, typeContract: null }],
      variables: [],
    })
  })

  afterEach(() => {
    useWorkflowCanvasStore.getState().reset()
    vi.unstubAllGlobals()
  })

  it('has no automatically detectable a11y violations with a small graph loaded', async () => {
    const { container, findByTestId } = render(<WorkflowCanvas />)

    await findByTestId('rf__node-start')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
