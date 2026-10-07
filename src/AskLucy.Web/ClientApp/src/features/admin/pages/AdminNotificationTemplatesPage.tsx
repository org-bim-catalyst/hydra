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
import { useT } from '../../../i18n/useT'
import type { NotificationCategory, NotificationChannel } from '../api/adminNotificationsApi'
import { AdminShell } from '../components/AdminShell'
import { useNotificationTemplates } from '../hooks/useAdminNotificationTemplates'
import { useOuterT } from '../hooks/useOuterT'
import { categoryLabel, channelLabel, errorText } from '../notificationAdminText'

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

function TemplatesContent() {
  const t = useT('admin.notifications')
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
    <Paper elevation={1} sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
        <TextField
          select
          size="small"
          label={t('templates.filters.category')}
          value={category}
          onChange={(e) => setCategory(e.target.value)}
          sx={{ minWidth: 160 }}
        >
          <MenuItem value="">{t('templates.filters.all')}</MenuItem>
          {CATEGORIES.map((c) => (
            <MenuItem key={c} value={c}>
              {categoryLabel(t, c)}
            </MenuItem>
          ))}
        </TextField>
        <TextField
          select
          size="small"
          label={t('templates.filters.channel')}
          value={channel}
          onChange={(e) => setChannel(e.target.value)}
          sx={{ minWidth: 140 }}
        >
          <MenuItem value="">{t('templates.filters.all')}</MenuItem>
          <MenuItem value="Email">{channelLabel(t, 'Email')}</MenuItem>
          <MenuItem value="InApp">{channelLabel(t, 'InApp')}</MenuItem>
        </TextField>
        <TextField
          size="small"
          label={t('templates.filters.language')}
          value={language}
          onChange={(e) => setLanguage(e.target.value)}
          sx={{ width: 120 }}
        />
        <TextField
          size="small"
          label={t('templates.filters.type')}
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
              {t('actions.retry')}
            </Button>
          }
        >
          {errorText(t, templates.error)}
        </Alert>
      )}

      <TableContainer>
        <Table size="small" aria-label={t('templates.table.aria')}>
          <TableHead>
            <TableRow>
              <TableCell>{t('templates.table.template')}</TableCell>
              <TableCell>{t('templates.table.type')}</TableCell>
              <TableCell>{t('templates.table.channel')}</TableCell>
              <TableCell>{t('templates.table.language')}</TableCell>
              <TableCell>{t('templates.table.published')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.templateId} hover>
                <TableCell>
                  <Button
                    component={RouterLink}
                    to={`/admin/notifications/templates/${row.templateId}`}
                    size="small"
                    sx={{ textTransform: 'none' }}
                  >
                    {row.name}
                  </Button>
                </TableCell>
                <TableCell>
                  <Typography variant="body2">
                    <bdi dir="ltr">{row.type}</bdi>
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    {categoryLabel(t, row.category)}
                  </Typography>
                </TableCell>
                <TableCell>{channelLabel(t, row.channel)}</TableCell>
                <TableCell>
                  <bdi dir="ltr">{row.language}</bdi>
                </TableCell>
                <TableCell>
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <span>
                      {row.publishedVersion
                        ? t('templates.versionShort', { number: row.publishedVersion.versionNumber })
                        : t('templates.none')}
                    </span>
                    {row.hasDraft && <Chip size="small" color="info" label={t('templates.draftWaiting')} />}
                  </Stack>
                </TableCell>
              </TableRow>
            ))}
            {!templates.isLoading && rows.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>
                  <Typography variant="body2" color="text.secondary">
                    {t('templates.empty')}
                  </Typography>
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
      {templates.isLoading && <CircularProgress size={24} aria-label={t('templates.loading')} />}
    </Paper>
  )
}

/** specs/067 US7 — the template list: one row per type, channel and language, with what is published and whether a draft is waiting. */
export function AdminNotificationTemplatesPage() {
  const t = useOuterT('admin.notifications')
  return (
    <AdminShell title={t('templates.title')} subtitle={t('templates.subtitle')}>
      <TemplatesContent />
    </AdminShell>
  )
}
