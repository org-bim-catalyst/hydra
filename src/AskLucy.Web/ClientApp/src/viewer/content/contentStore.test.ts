import { describe, expect, it } from 'vitest'
import { useContentStore } from './contentStore'
import type { ViewerContent } from './ViewerContent'

function makeContent(overrides: Partial<ViewerContent> = {}): ViewerContent {
  return {
    id: 'content-1',
    layerId: 'layer-1',
    source: { kind: 'gis', provider: 'google-maps', center: { latitude: 25, longitude: 55 } },
    placement: null,
    loadState: 'loading',
    failureReason: null,
    ...overrides,
  }
}

describe('contentStore (data-model.md "Content Load Lifecycle")', () => {
  it('upsert adds new content and transitions loading -> loaded', () => {
    useContentStore.getState().upsert(makeContent({ loadState: 'loading' }))
    expect(useContentStore.getState().content[0].loadState).toBe('loading')

    useContentStore.getState().upsert(makeContent({ loadState: 'loaded' }))
    expect(useContentStore.getState().content).toHaveLength(1)
    expect(useContentStore.getState().content[0].loadState).toBe('loaded')
  })

  it('an unsupported format resolves to failed/unsupported-format without throwing', () => {
    expect(() =>
      useContentStore.getState().upsert(
        makeContent({ loadState: 'failed', failureReason: 'unsupported-format' }),
      ),
    ).not.toThrow()

    const content = useContentStore.getState().content.find((c) => c.id === 'content-1')
    expect(content?.loadState).toBe('failed')
    expect(content?.failureReason).toBe('unsupported-format')
  })

  it('remove drops content by id, leaving others untouched', () => {
    useContentStore.getState().upsert(makeContent({ id: 'a' }))
    useContentStore.getState().upsert(makeContent({ id: 'b' }))

    useContentStore.getState().remove('a')

    const ids = useContentStore.getState().content.map((c) => c.id)
    expect(ids).not.toContain('a')
    expect(ids).toContain('b')
  })
})
