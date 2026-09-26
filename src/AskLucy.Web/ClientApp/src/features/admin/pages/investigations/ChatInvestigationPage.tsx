import { Alert, Box, Button, Chip, CircularProgress, Link, Paper, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink, useParams } from 'react-router'
import { ApiError } from '../../../../api/httpClient'
import { MessageBubble } from '../../../chat/components/MessageBubble'
import * as operationalFailuresApi from '../../api/adminOperationalFailuresApi'
import type { ChatInvestigation } from '../../api/adminOperationalFailuresApi'
import { AdminShell } from '../../components/AdminShell'
import { formatWhen } from '../../components/operationalFailures/operationalFailureLabels'
import { UserRefLink } from '../../components/operationalFailures/UserRefLink'

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : 'Could not load this chat.'

function Transcript({ investigation }: { investigation: ChatInvestigation }) {
  if (investigation.chat.deleted) {
    return <Alert severity="info">This chat was deleted.</Alert>
  }

  if (investigation.transcript === null) {
    return (
      <Alert severity="info">
        Content is visible only to staff with <em>View user content</em>.
      </Alert>
    )
  }

  return (
    <Stack spacing={1.5}>
      {investigation.transcript.map((message) =>
        // No id and no handlers: MessageBubble then renders the content alone, with no action row.
        message.isFailedTurn ? (
          <Box
            key={message.id}
            data-testid="failed-turn"
            sx={{ border: 1, borderColor: 'error.main', borderRadius: 1, p: 1 }}
          >
            <Chip size="small" color="error" variant="outlined" label="Failed turn" sx={{ mb: 1 }} />
            <MessageBubble message={{ role: message.role, content: message.content }} />
          </Box>
        ) : (
          <MessageBubble key={message.id} message={{ role: message.role, content: message.content }} />
        ),
      )}
    </Stack>
  )
}

/**
 * specs/074 research D15 — a chat as seen from a failure incident. Read-only by construction:
 * there is no composer and no action on any message. The server decides whether the transcript is
 * included (it needs *View user content*, and every such read is audited); this page only shows
 * what it was given.
 */
export function ChatInvestigationPage() {
  const { incidentId = '', chatId = '' } = useParams()
  const query = useQuery({
    queryKey: operationalFailuresApi.OPERATIONAL_FAILURE_QUERY_KEYS.chatInvestigation(incidentId, chatId),
    queryFn: () => operationalFailuresApi.getChatInvestigation(incidentId, chatId),
  })
  const investigation = query.data

  return (
    <AdminShell title="Chat investigation" subtitle="A read-only view of a chat referenced by a failure incident">
      <Stack spacing={2} sx={{ maxWidth: 900 }}>
        <Link component={RouterLink} to="/admin/operational-failures" variant="body2">
          Back to operational failures
        </Link>

        {query.isLoading && <CircularProgress size={24} aria-label="Loading chat" />}
        {query.isError && (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => void query.refetch()}>
                Retry
              </Button>
            }
          >
            {errorMessage(query.error)}
          </Alert>
        )}

        {investigation && (
          <>
            <Paper elevation={1} sx={{ p: 2 }}>
              <Typography variant="h6" component="h2">
                {investigation.chat.title}
              </Typography>
              <Typography variant="body2" color="text.secondary" component="div">
                Owner: <UserRefLink user={investigation.chat.owner} />
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Started {formatWhen(investigation.chat.createdAtUtc)} · last activity{' '}
                {formatWhen(investigation.chat.lastActivityUtc)} · {investigation.chat.messageCount} messages
              </Typography>
            </Paper>

            <Paper elevation={1} sx={{ p: 2 }}>
              <Typography variant="subtitle2" component="h3" sx={{ mb: 1 }}>
                Where it failed
              </Typography>
              <Stack component="ul" spacing={0.5} sx={{ m: 0, pl: 2.5 }}>
                {investigation.failurePoints.map((point) => (
                  <Typography component="li" variant="body2" key={point.occurrenceId}>
                    {point.turnNumber === null ? 'Turn unknown' : `Turn ${point.turnNumber}`} ·{' '}
                    {formatWhen(point.occurredAtUtc)}
                  </Typography>
                ))}
              </Stack>
            </Paper>

            <Transcript investigation={investigation} />
          </>
        )}
      </Stack>
    </AdminShell>
  )
}
