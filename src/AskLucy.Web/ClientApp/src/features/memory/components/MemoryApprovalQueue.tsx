import CheckIcon from '@mui/icons-material/Check'
import CloseIcon from '@mui/icons-material/Close'
import LockIcon from '@mui/icons-material/Lock'
import { Alert, Box, Button, Card, CardContent, Chip, Snackbar, Stack, Typography } from '@mui/material'
import { useMemo, useState } from 'react'
import { EmptyState } from '../../../components/EmptyState'
import { usePendingMemories } from '../hooks/useMemories'
import { useApproveMemory, useRejectMemory } from '../hooks/useMemoryMutations'

/**
 * spec.md FR-021, User Story 3 AC1/AC2/AC3 — every memory candidate currently held for manual
 * review (Manual-mode categories, plus any candidate flagged sensitive regardless of mode,
 * FR-008), with one-click approve/reject.
 */
export function MemoryApprovalQueue() {
  const { data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage } = usePendingMemories()
  const approveMemory = useApproveMemory()
  const rejectMemory = useRejectMemory()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const pending = useMemo(() => data?.pages.flatMap((page) => page.results) ?? [], [data])

  const reportError = (err: unknown) => setErrorMessage(err instanceof Error ? err.message : 'Action failed. Please try again.')

  if (!isLoading && pending.length === 0) {
    return <EmptyState icon={<CheckIcon fontSize="inherit" />} title="Nothing waiting for review" description="Candidates held for manual approval will show up here." />
  }

  return (
    <Box>
      <Stack sx={{ gap: 1 }}>
        {pending.map((memory) => (
          <Card key={memory.id} variant="outlined" data-testid="approval-queue-item">
            <CardContent>
              <Stack direction="row" sx={{ alignItems: 'flex-start', justifyContent: 'space-between', gap: 2 }}>
                <Box sx={{ flex: 1 }}>
                  <Typography variant="body1">{memory.content}</Typography>
                  <Stack direction="row" spacing={1} sx={{ mt: 1, alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
                    <Chip size="small" variant="outlined" label={memory.category} />
                    {memory.isSensitive && (
                      <Chip size="small" icon={<LockIcon fontSize="small" />} label="Sensitive" color="error" variant="outlined" />
                    )}
                  </Stack>
                </Box>
                <Stack direction="row" spacing={1}>
                  <Button
                    size="small"
                    variant="outlined"
                    color="error"
                    startIcon={<CloseIcon />}
                    onClick={() => rejectMemory.mutate(memory.id, { onError: reportError })}
                  >
                    Reject
                  </Button>
                  <Button
                    size="small"
                    variant="contained"
                    startIcon={<CheckIcon />}
                    onClick={() => approveMemory.mutate(memory.id, { onError: reportError })}
                  >
                    Approve
                  </Button>
                </Stack>
              </Stack>
            </CardContent>
          </Card>
        ))}
      </Stack>

      {hasNextPage && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
          <Button variant="outlined" onClick={() => fetchNextPage()} loading={isFetchingNextPage}>
            Load more
          </Button>
        </Box>
      )}

      <Snackbar open={Boolean(errorMessage)} autoHideDuration={5000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </Box>
  )
}
