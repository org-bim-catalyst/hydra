import {
  Box,
  Chip,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import { getVoiceProviderHealth } from '../../chat/api/voiceApi'

const DIRECTION_LABEL: Record<string, string> = {
  FailedOverToFallback: 'Failed over to fallback',
  RecoveredToPrimary: 'Recovered to primary',
}

/**
 * specs/012-elevenlabs-voice-engine T077 (FR-039/SC-011, contracts/voice-provider-health.md)
 * — a read-only panel on the existing AI-provider health page, not a new admin page, so
 * repeated ElevenLabs failovers are visible alongside the chat-provider health an admin
 * already checks here (last 24h, same window the backend defaults to when unspecified).
 */
export function VoiceFailoverPanel() {
  const { data: health, isLoading } = useQuery({
    queryKey: ['admin', 'voice-provider-health'],
    queryFn: () => getVoiceProviderHealth(),
  })

  return (
    <Paper elevation={1} sx={{ mt: 3 }}>
      <Box sx={{ p: 2, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Box>
          <Typography variant="h6">Voice provider failover (last 24h)</Typography>
          <Typography variant="body2" color="text.secondary">
            ElevenLabs voice conversations falling back to the legacy STT/TTS implementation.
          </Typography>
        </Box>
        {health && (
          <Chip
            label={health.currentStatus === 'degraded' ? 'Degraded' : 'Healthy'}
            color={health.currentStatus === 'degraded' ? 'error' : 'success'}
            variant="outlined"
          />
        )}
      </Box>
      <TableContainer>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Occurred</TableCell>
              <TableCell>Direction</TableCell>
              <TableCell>Reason</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={3} />}
            {health?.events.map((event, index) => (
              <TableRow key={`${event.occurredAtUtc}-${index}`}>
                <TableCell>{new Date(event.occurredAtUtc).toLocaleString()}</TableCell>
                <TableCell>{DIRECTION_LABEL[event.direction] ?? event.direction}</TableCell>
                <TableCell>{event.reason ?? '—'}</TableCell>
              </TableRow>
            ))}
            {!isLoading && health?.events.length === 0 && (
              <TableEmptyRow colSpan={3} message="No failovers in the last 24 hours." />
            )}
          </TableBody>
        </Table>
      </TableContainer>
    </Paper>
  )
}
