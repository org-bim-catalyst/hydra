import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { useComingSoonStore } from '../../store/comingSoonStore'
import { ComingSoonDialog } from './ComingSoonDialog'

function resetStore() {
  useComingSoonStore.setState({ featureLabel: null })
}

describe('ComingSoonDialog', () => {
  beforeEach(() => {
    resetStore()
  })

  it('is closed when no feature is set', () => {
    render(<ComingSoonDialog />)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('opens and names the feature once show() is called', () => {
    // container.querySelector (not getByRole) here: jsdom's CSS engine has a known issue
    // resolving computed styles for MUI Dialog's Modal/Backdrop/Grow transition tree when
    // getByRole's accessibility-tree walk runs against it.
    useComingSoonStore.getState().show('Layers')
    const { container } = render(<ComingSoonDialog />)
    expect(container.ownerDocument.querySelector('[role="dialog"]')).toBeInTheDocument()
    expect(container.ownerDocument.body).toHaveTextContent('Layers is coming soon to the Studio workspace.')
  })
})
