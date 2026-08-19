import { beforeEach, describe, expect, it } from 'vitest'
import { useComingSoonStore } from './comingSoonStore'

function resetStore() {
  useComingSoonStore.setState({ featureLabel: null })
}

describe('comingSoonStore', () => {
  beforeEach(() => {
    resetStore()
  })

  it('defaults to no feature shown', () => {
    expect(useComingSoonStore.getState().featureLabel).toBeNull()
  })

  it('show(label) sets the feature label', () => {
    useComingSoonStore.getState().show('Layers')
    expect(useComingSoonStore.getState().featureLabel).toBe('Layers')
  })

  it('hide() clears the feature label', () => {
    useComingSoonStore.getState().show('Layers')
    useComingSoonStore.getState().hide()
    expect(useComingSoonStore.getState().featureLabel).toBeNull()
  })
})
