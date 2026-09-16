import { useEffect, useMemo, useState } from 'react'

export interface UseBulkSelectionResult {
  selected: ReadonlySet<string>
  selectedCount: number
  isAllSelected: boolean
  isIndeterminate: boolean
  toggleAll: () => void
  toggleOne: (id: string) => void
  clear: () => void
}

/** Page-scoped bulk selection over `rowIds`. Resets whenever the `rowIds` array reference changes
 * (e.g. a new page or filtered search result), so a stale selection never survives a data refresh. */
export function useBulkSelection(rowIds: string[]): UseBulkSelectionResult {
  const [selected, setSelected] = useState<Set<string>>(new Set())

  useEffect(() => {
    setSelected(new Set())
  }, [rowIds])

  const selectableIds = useMemo(() => rowIds, [rowIds])

  const selectedCount = selected.size
  const isAllSelected = selectableIds.length > 0 && selectedCount === selectableIds.length
  const isIndeterminate = selectedCount > 0 && !isAllSelected

  function toggleAll() {
    setSelected(isAllSelected ? new Set() : new Set(selectableIds))
  }

  function toggleOne(id: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  function clear() {
    setSelected(new Set())
  }

  return { selected, selectedCount, isAllSelected, isIndeterminate, toggleAll, toggleOne, clear }
}
