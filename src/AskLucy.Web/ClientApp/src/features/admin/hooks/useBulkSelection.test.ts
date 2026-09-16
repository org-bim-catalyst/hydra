import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useBulkSelection } from './useBulkSelection'

describe('useBulkSelection', () => {
  it('starts with nothing selected', () => {
    const { result } = renderHook(() => useBulkSelection(['a', 'b', 'c']))

    expect(result.current.selectedCount).toBe(0)
    expect(result.current.isAllSelected).toBe(false)
    expect(result.current.isIndeterminate).toBe(false)
  })

  it('toggleOne selects and deselects a single row', () => {
    const { result } = renderHook(() => useBulkSelection(['a', 'b', 'c']))

    act(() => result.current.toggleOne('b'))
    expect(result.current.selected.has('b')).toBe(true)
    expect(result.current.isIndeterminate).toBe(true)

    act(() => result.current.toggleOne('b'))
    expect(result.current.selected.has('b')).toBe(false)
    expect(result.current.selectedCount).toBe(0)
  })

  it('toggleAll selects every row, then deselects all on a second call', () => {
    const { result } = renderHook(() => useBulkSelection(['a', 'b', 'c']))

    act(() => result.current.toggleAll())
    expect(result.current.selectedCount).toBe(3)
    expect(result.current.isAllSelected).toBe(true)
    expect(result.current.isIndeterminate).toBe(false)

    act(() => result.current.toggleAll())
    expect(result.current.selectedCount).toBe(0)
  })

  it('goes indeterminate when a full-page selection is partially deselected', () => {
    const { result } = renderHook(() => useBulkSelection(['a', 'b', 'c']))

    act(() => result.current.toggleAll())
    act(() => result.current.toggleOne('a'))

    expect(result.current.isAllSelected).toBe(false)
    expect(result.current.isIndeterminate).toBe(true)
  })

  it('clear empties the selection', () => {
    const { result } = renderHook(() => useBulkSelection(['a', 'b', 'c']))

    act(() => result.current.toggleAll())
    act(() => result.current.clear())

    expect(result.current.selectedCount).toBe(0)
  })

  it('resets selection when rowIds reference changes', () => {
    let rowIds = ['a', 'b', 'c']
    const { result, rerender } = renderHook(() => useBulkSelection(rowIds))

    act(() => result.current.toggleAll())
    expect(result.current.selectedCount).toBe(3)

    rowIds = ['d', 'e']
    rerender()

    expect(result.current.selectedCount).toBe(0)
  })
})
