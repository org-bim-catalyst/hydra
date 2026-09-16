import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useBulkSelection } from './useBulkSelection'

const PAGE = ['a', 'b', 'c']

describe('useBulkSelection', () => {
  it('starts with nothing selected', () => {
    const { result } = renderHook(() => useBulkSelection())

    expect(result.current.selectedCount()).toBe(0)
    expect(result.current.pageState(PAGE)).toBe('none')
  })

  it('toggleOne selects and deselects a single row', () => {
    const { result } = renderHook(() => useBulkSelection())

    act(() => result.current.toggleOne('b'))
    expect(result.current.isSelected('b')).toBe(true)
    expect(result.current.pageState(PAGE)).toBe('partial')

    act(() => result.current.toggleOne('b'))
    expect(result.current.isSelected('b')).toBe(false)
    expect(result.current.selectedCount()).toBe(0)
  })

  it('selectPageOnly selects every row on that page; deselectPageOnly clears them again', () => {
    const { result } = renderHook(() => useBulkSelection())

    act(() => result.current.selectPageOnly(PAGE))
    expect(result.current.selectedCount()).toBe(3)
    expect(result.current.pageState(PAGE)).toBe('all')

    act(() => result.current.deselectPageOnly(PAGE))
    expect(result.current.selectedCount()).toBe(0)
    expect(result.current.pageState(PAGE)).toBe('none')
  })

  it('goes indeterminate when a full-page selection is partially deselected', () => {
    const { result } = renderHook(() => useBulkSelection())

    act(() => result.current.selectPageOnly(PAGE))
    act(() => result.current.toggleOne('a'))

    expect(result.current.pageState(PAGE)).toBe('partial')
  })

  it('deselectAll empties the selection', () => {
    const { result } = renderHook(() => useBulkSelection())

    act(() => result.current.selectPageOnly(PAGE))
    act(() => result.current.deselectAll())

    expect(result.current.selectedCount()).toBe(0)
    expect(result.current.isAllMatching).toBe(false)
  })

  it('selection survives navigating away from a page and back (persists across pagination)', () => {
    const { result } = renderHook(() => useBulkSelection())
    const page1 = ['a', 'b']
    const page2 = ['d', 'e']

    act(() => result.current.selectPageOnly(page1))
    // Simulates paging to page 2, then back to page 1 — page1's ids are never touched in between.
    expect(result.current.pageState(page2)).toBe('none')
    expect(result.current.pageState(page1)).toBe('all')
  })

  it('selectAllMatching selects everything, including rows outside the current page', () => {
    const { result } = renderHook(() => useBulkSelection())

    act(() => result.current.selectAllMatching())

    expect(result.current.isAllMatching).toBe(true)
    expect(result.current.isSelected('anything-not-on-any-known-page')).toBe(true)
    expect(result.current.selectedCount(42)).toBe(42)
  })

  it('an explicit exception survives while in "all matching" mode', () => {
    const { result } = renderHook(() => useBulkSelection())

    act(() => result.current.selectAllMatching())
    act(() => result.current.toggleOne('b'))

    expect(result.current.isSelected('b')).toBe(false)
    expect(result.current.isSelected('a')).toBe(true)
    expect(result.current.selectedCount(3)).toBe(2)
  })

  it('deselectPageOnly while "all matching" carves that page out without leaving all-matching mode', () => {
    const { result } = renderHook(() => useBulkSelection())

    act(() => result.current.selectAllMatching())
    act(() => result.current.deselectPageOnly(PAGE))

    expect(result.current.isAllMatching).toBe(true)
    expect(result.current.pageState(PAGE)).toBe('none')
  })
})
