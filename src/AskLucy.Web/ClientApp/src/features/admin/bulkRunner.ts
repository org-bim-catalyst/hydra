export interface BulkActionSkip {
  id: string
  reason: string
}

export interface BulkActionOutcome {
  succeededCount: number
  skipped: BulkActionSkip[]
}

const BATCH_SIZE = 20

/**
 * Runs `run` over `ids` in fixed-size batches, sequentially (not in parallel — that's what lets
 * `onProgress` reflect real, incremental completion instead of jumping straight from 0 to N),
 * aggregating every batch's outcome into one. Backs the "Deleting {done} of {total}" progress
 * dialog — the bulk endpoints themselves are single-shot per call, so batching client-side is
 * what turns that into visible progress for a large selection.
 */
export async function runBatchedBulkAction(
  ids: string[],
  run: (batchIds: string[]) => Promise<BulkActionOutcome>,
  onProgress: (done: number, total: number) => void,
): Promise<BulkActionOutcome> {
  const total = ids.length
  let succeededCount = 0
  const skipped: BulkActionSkip[] = []

  onProgress(0, total)

  for (let start = 0; start < ids.length; start += BATCH_SIZE) {
    const batch = ids.slice(start, start + BATCH_SIZE)
    const outcome = await run(batch)
    succeededCount += outcome.succeededCount
    skipped.push(...outcome.skipped)
    onProgress(Math.min(start + batch.length, total), total)
  }

  return { succeededCount, skipped }
}
