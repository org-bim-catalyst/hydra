import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import { ExtensionHudItemHost } from './ExtensionHudItemHost'

const initialState = useViewerExtensionStore.getState()

function addHudItem(extensionId: string, testId: string) {
  useViewerExtensionStore.getState().addContribution({
    kind: 'hudItem',
    extensionId,
    component: () => <span data-testid={testId}>{testId}</span>,
  })
}

describe('ExtensionHudItemHost (specs/073 contract X2–X7)', () => {
  beforeEach(() => {
    useViewerExtensionStore.setState(initialState, true)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders hudItem contributions in contribution order, as direct children with no wrapper (X5, X6)', () => {
    addHudItem('ext-a', 'item-a')
    addHudItem('ext-b', 'item-b')
    addHudItem('ext-a', 'item-a2')

    const { container } = render(<ExtensionHudItemHost />)

    const children = Array.from(container.children).map((el) => el.getAttribute('data-testid'))
    expect(children).toEqual(['item-a', 'item-b', 'item-a2'])
  })

  it('ignores overlay contributions and kinds it does not recognise (X4)', () => {
    useViewerExtensionStore.getState().addContribution({
      kind: 'overlay',
      extensionId: 'ext-o',
      component: () => <span>overlay</span>,
    })
    useViewerExtensionStore.getState().addContribution({
      // @ts-expect-error — deliberately not a declared Contribution kind.
      kind: 'futureKind',
      extensionId: 'ext-future',
    })

    const { container } = render(<ExtensionHudItemHost />)

    expect(container).toBeEmptyDOMElement()
  })

  it('renders an item contributed before the host mounted (X2)', () => {
    addHudItem('early', 'item-early')

    render(<ExtensionHudItemHost />)

    expect(screen.getByTestId('item-early')).toBeInTheDocument()
  })

  it('drops the item once its extension is withdrawn (X3)', () => {
    addHudItem('ext-c', 'item-c')
    render(<ExtensionHudItemHost />)
    expect(screen.getByTestId('item-c')).toBeInTheDocument()

    act(() => useViewerExtensionStore.getState().removeContributionsFor('ext-c'))

    expect(screen.queryByTestId('item-c')).not.toBeInTheDocument()
  })

  it('contains a throwing item: it marks its extension failed and the other items keep rendering (X7)', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    useViewerExtensionStore.getState().addContribution({
      kind: 'hudItem',
      extensionId: 'ext-broken',
      component: () => {
        throw new Error('bad item')
      },
    })
    addHudItem('ext-ok', 'item-ok')

    render(<ExtensionHudItemHost />)

    expect(screen.getByTestId('item-ok')).toBeInTheDocument()
    expect(useViewerExtensionStore.getState().extensions['ext-broken']).toMatchObject({
      lifecycle: 'failed',
      failureReason: 'Render failed: bad item',
    })
  })
})
