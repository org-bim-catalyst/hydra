import { describe, expect, it, vi } from 'vitest'
import { runBatchedBulkAction } from './bulkRunner'

describe('runBatchedBulkAction', () => {
  it('runs a single batch when ids fit within the batch size', async () => {
    const run = vi.fn().mockResolvedValue({ succeededCount: 3, skipped: [] })
    const onProgress = vi.fn()

    const outcome = await runBatchedBulkAction(['a', 'b', 'c'], run, onProgress)

    expect(run).toHaveBeenCalledTimes(1)
    expect(run).toHaveBeenCalledWith(['a', 'b', 'c'])
    expect(outcome).toEqual({ succeededCount: 3, skipped: [] })
    expect(onProgress).toHaveBeenCalledWith(0, 3)
    expect(onProgress).toHaveBeenLastCalledWith(3, 3)
  })

  it('splits a large id list into multiple sequential batches and aggregates the outcomes', async () => {
    const ids = Array.from({ length: 45 }, (_, i) => `id-${i}`)
    const run = vi.fn().mockImplementation(async (batch: string[]) => ({
      succeededCount: batch.length - 1,
      skipped: [{ id: batch[0], reason: 'AlreadyLocked' }],
    }))
    const onProgress = vi.fn()

    const outcome = await runBatchedBulkAction(ids, run, onProgress)

    expect(run).toHaveBeenCalledTimes(3) // 20 + 20 + 5
    expect(outcome.succeededCount).toBe(19 + 19 + 4)
    expect(outcome.skipped).toHaveLength(3)
    expect(onProgress).toHaveBeenCalledWith(20, 45)
    expect(onProgress).toHaveBeenCalledWith(40, 45)
    expect(onProgress).toHaveBeenLastCalledWith(45, 45)
  })

  it('returns an empty outcome and never calls run for an empty id list', async () => {
    const run = vi.fn()
    const onProgress = vi.fn()

    const outcome = await runBatchedBulkAction([], run, onProgress)

    expect(run).not.toHaveBeenCalled()
    expect(outcome).toEqual({ succeededCount: 0, skipped: [] })
    expect(onProgress).toHaveBeenCalledWith(0, 0)
  })
})
