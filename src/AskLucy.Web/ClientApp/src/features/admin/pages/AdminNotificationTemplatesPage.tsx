import {
  Alert,
  Button,
  Chip,
  CircularProgress,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import { ApiError } from '../../../api/httpClient'
import type { NotificationCategory, NotificationChannel } from '../api/adminNotificationsApi'
import { AdminShell } from '../components/AdminShell'
import { useNotificationTemplates } from '../hooks/useAdminNotificationTemplates'

const CATEGORIES: NotificationCategory[] = [
  'Security',
  'Account',
  'Agent',
  'Workflow',
  'Document',
  'KnowledgeBase',
  'Memory',
  'System',
  'Billing',
  'Conversation',
]
const CHANNEL_LABEL: Record<NotificationChannel, string> = { InApp: 'In-app', Email: 'Email' }

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.'

/** specs/067 US7 — the template list: one row per type, channel and language, with what is published and whether a draft is waiting. */
export function AdminNotificationTemplatesPage() {
  const [category, setCategory] = useState('')
  const [channel, setChannel] = useState('')
  const [language, setLanguage] = useState('')
  const [type, setType] = useState('')
  const templates = useNotificationTemplates({
    category: (category || undefined) as NotificationCategory | undefined,
    channel: (channel || undefined) as NotificationChannel | undefined,
    language: language.trim() || undefined,
    type: type.trim() || undefined,
  })
  const rows = templates.data ?? []

  return (
    <AdminShell
      title="Notification templates"
      subtitle="The wording of every notification, in every language"
    >
      <Paper elevation={1} sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          <TextField
            select
            size="small"
            label="Category"
            value={category}
            onChange={(e) => setCategory(e.target.value)}
            sx={{ minWidth: 160 }}
          >
            <MenuItem value="">All</MenuItem>
            {CATEGORIES.map((c) => (
              <MenuItem key={c} value={c}>
                {c}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            select
            size="small"
            label="Channel"
            value={channel}
            onChange={(e) => setChannel(e.target.value)}
            sx={{ minWidth: 140 }}
          >
            <MenuItem value="">All</MenuItem>
            <MenuItem value="Email">Email</MenuItem>
            <MenuItem value="InApp">In-app</MenuItem>
          </TextField>
          <TextField
            size="small"
            label="Language"
            value={language}
            onChange={(e) => setLanguage(e.target.value)}
            sx={{ width: 120 }}
          />
          <TextField
            size="small"
            label="Type"
            value={type}
            onChange={(e) => setType(e.target.value)}
            sx={{ minWidth: 220 }}
          />
        </Stack>

        {templates.isError && (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => void templates.refetch()}>
                Retry
              </Button>
            }
          >
            {errorMessage(templates.error)}
          </Alert>
        )}

        <TableContainer>
          <Table size="small" aria-label="Notification templates">
            <TableHead>
              <TableRow>
                <TableCell>Template</TableCell>
                <TableCell>Type</TableCell>
                <TableCell>Channel</TableCell>
                <TableCell>Language</TableCell>
                <TableCell>Published</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((t) => (
                <TableRow key={t.templateId} hover>
                  <TableCell>
                    <Button
                      component={RouterLink}
                      to={`/admin/notifications/templates/${t.templateId}`}
                      size="small"
                      sx={{ textTransform: 'none' }}
                    >
                      {t.name}
                    </Button>
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2">{t.type}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {t.category}
                    </Typography>
                  </TableCell>
                  <TableCell>{CHANNEL_LABEL[t.channel]}</TableCell>
                  <TableCell>{t.language}</TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                      <span>
                        {t.publishedVersion ? `v${t.publishedVersion.versionNumber}` : 'None'}
                      </span>
                      {t.hasDraft && <Chip size="small" color="info" label="Draft waiting" />}
                    </Stack>
                  </TableCell>
                </TableRow>
              ))}
              {!templates.isLoading && rows.length === 0 && (
                <TableRow>
                  <TableCell colSpan={5}>
                    <Typography variant="body2" color="text.secondary">
                      No templates match.
                    </Typography>
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
        {templates.isLoading && <CircularProgress size={24} aria-label="Loading templates" />}
      </Paper>
    </AdminShell>
  )
}
