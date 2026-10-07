import { useEffect, useMemo, useRef, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  List,
  ListItem,
  ListItemButton,
  ListItemText,
  Paper,
  Snackbar,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import { Controller, useFieldArray, useForm } from 'react-hook-form'
import { Link as RouterLink, useParams } from 'react-router'
import { z } from 'zod'
import { ApiError } from '../../../api/httpClient'
import { useFormat, useT } from '../../../i18n/useT'
import type {
  NotificationTemplateDetail,
  TemplateVersion,
  TemplateVersionInput,
  TemplateVersionStatus,
} from '../api/adminNotificationTemplatesApi'
import { AdminShell } from '../components/AdminShell'
import { templateTextProblem } from '../forms/templateText'
import { zodResolver } from '../forms/zodResolver'
import { useCanManageNotifications } from '../hooks/useAdminNotifications'
import { useOuterT } from '../hooks/useOuterT'
import {
  channelLabel,
  DATE_ONLY,
  errorText,
  PLAIN_NUMBER,
  type NotificationAdminT,
} from '../notificationAdminText'
import {
  useArchiveTemplateVersion,
  useCreateTemplateDraft,
  useNotificationTemplate,
  usePreviewTemplateVersion,
  usePublishTemplateVersion,
  useSendTemplateTest,
  useTemplateVersion,
  useUpdateTemplateDraft,
} from '../hooks/useAdminNotificationTemplates'

const STATUS_COLOR: Record<TemplateVersionStatus, 'default' | 'info' | 'success'> = {
  Draft: 'info',
  Published: 'success',
  Archived: 'default',
}

function text(
  t: NotificationAdminT,
  maxLength: number,
  allowed: ReadonlySet<string>,
  required: boolean,
) {
  return z.string().superRefine((value, ctx) => {
    if (required && value.trim() === '') {
      ctx.addIssue({ code: 'custom', message: t('templateText.required') })
      return
    }
    const problem = templateTextProblem(value, maxLength, allowed, t)
    if (problem) ctx.addIssue({ code: 'custom', message: problem })
  })
}

function buildSchema(t: NotificationAdminT, channel: 'Email' | 'InApp', allowed: ReadonlySet<string>) {
  const email = channel === 'Email'
  return z.object({
    subject: email ? text(t, 200, allowed, true) : z.string(),
    preheader: email ? text(t, 200, allowed, false) : z.string(),
    greeting: email ? text(t, 200, allowed, false) : z.string(),
    heading: email ? text(t, 200, allowed, true) : z.string(),
    paragraphs: z
      .array(z.object({ text: email ? text(t, 1000, allowed, true) : z.string() }))
      .refine((p) => !email || (p.length >= 1 && p.length <= 10), {
        message: t('templateText.paragraphsRange'),
      }),
    safetyNote: email ? text(t, 500, allowed, true) : z.string(),
    footerNote: email ? text(t, 500, allowed, false) : z.string(),
    title: email ? z.string() : text(t, 200, allowed, true),
    message: email ? z.string() : text(t, 1000, allowed, true),
    actionLabel: text(t, 60, allowed, false),
  })
}

type TemplateForm = z.infer<ReturnType<typeof buildSchema>>

const toForm = (v: TemplateVersion): TemplateForm => ({
  subject: v.subject ?? '',
  preheader: v.preheader ?? '',
  greeting: v.greeting ?? '',
  heading: v.heading ?? '',
  paragraphs: v.bodyParagraphs.map((p) => ({ text: p })),
  safetyNote: v.safetyNote ?? '',
  footerNote: v.footerNote ?? '',
  title: v.title ?? '',
  message: v.message ?? '',
  actionLabel: v.actionLabel ?? '',
})

const toInput = (channel: 'Email' | 'InApp', f: TemplateForm): TemplateVersionInput =>
  channel === 'Email'
    ? {
        subject: f.subject,
        preheader: f.preheader,
        greeting: f.greeting,
        heading: f.heading,
        bodyParagraphs: f.paragraphs.map((p) => p.text),
        safetyNote: f.safetyNote,
        footerNote: f.footerNote,
        actionLabel: f.actionLabel,
      }
    : { title: f.title, message: f.message, actionLabel: f.actionLabel }

type FieldName =
  | 'subject'
  | 'preheader'
  | 'greeting'
  | 'heading'
  | 'safetyNote'
  | 'footerNote'
  | 'title'
  | 'message'
  | 'actionLabel'
  | `paragraphs.${number}.text`

interface ActionFailure {
  message: string
  /** A 409 means the version changed under the editor, so the only useful next step is to reload it. */
  conflict: boolean
}

const failureOf = (
  t: NotificationAdminT,
  key: 'saveDraft' | 'publish' | 'archive' | 'test',
  err: unknown,
): ActionFailure => ({
  message: t(`editor.failures.${key}`, { detail: errorText(t, err) }),
  conflict: err instanceof ApiError && err.status === 409,
})

// ---- the preview ----

function PreviewPanel({
  preview,
  isPending,
  error,
}: {
  preview: ReturnType<typeof usePreviewTemplateVersion>['data']
  isPending: boolean
  error: unknown
}) {
  const t = useT('admin.notifications')
  return (
    <Paper variant="outlined" sx={{ p: 2, minHeight: 160 }}>
      <Typography variant="subtitle2" gutterBottom>
        {t('editor.preview.title')}
      </Typography>
      {isPending && <CircularProgress size={20} aria-label={t('editor.preview.rendering')} />}
      {error !== null && error !== undefined && (
        <Alert severity="error">
          {t('editor.preview.failed', { detail: errorText(t, error) })}
        </Alert>
      )}
      {preview?.html != null && (
        <Stack spacing={1}>
          <Typography variant="body2">
            <strong>{t('editor.preview.subject')}</strong> {preview.subject}
          </Typography>
          {/* The rendered email is shown in a frame with every sandbox permission off: no scripts, no navigation, no same-origin access. */}
          <Box
            component="iframe"
            title={t('editor.preview.frameTitle')}
            sandbox=""
            srcDoc={preview.html}
            sx={{
              width: '100%',
              height: 420,
              border: 1,
              borderColor: 'divider',
              borderRadius: 1,
              bgcolor: '#fff',
            }}
          />
        </Stack>
      )}
      {preview?.html == null && preview?.title != null && (
        <Stack spacing={0.5} dir={preview.direction}>
          <Typography variant="subtitle1">{preview.title}</Typography>
          <Typography variant="body2">{preview.message}</Typography>
          {preview.actionLabel && (
            <Chip size="small" label={preview.actionLabel} sx={{ alignSelf: 'flex-start' }} />
          )}
        </Stack>
      )}
    </Paper>
  )
}

// ---- one version ----

interface VersionEditorProps {
  template: NotificationTemplateDetail
  version: TemplateVersion
  canManage: boolean
  onReload: () => void
}

function VersionEditor({ template, version, canManage, onReload }: VersionEditorProps) {
  const t = useT('admin.notifications')
  const format = useFormat()
  const channel = template.channel
  const isEmail = channel === 'Email'
  const editable = canManage && version.status === 'Draft'
  const allowed = useMemo(
    () => new Set(template.declaredVariables.map((v) => v.name)),
    [template.declaredVariables],
  )
  const schema = useMemo(() => buildSchema(t, channel, allowed), [t, channel, allowed])

  const { control, handleSubmit, formState, getValues, setValue } = useForm<TemplateForm>({
    resolver: zodResolver(schema),
    defaultValues: toForm(version),
    mode: 'onChange',
  })
  const paragraphs = useFieldArray({ control, name: 'paragraphs' })

  const update = useUpdateTemplateDraft(template.templateId)
  const publish = usePublishTemplateVersion(template.templateId)
  const archive = useArchiveTemplateVersion(template.templateId)
  const preview = usePreviewTemplateVersion(template.templateId)
  const sendTest = useSendTemplateTest(template.templateId)
  const { mutate: runPreview } = preview

  const [failure, setFailure] = useState<ActionFailure | null>(null)
  const [confirm, setConfirm] = useState<'publish' | 'archive' | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const focused = useRef<{
    name: FieldName
    element: HTMLInputElement | HTMLTextAreaElement
  } | null>(null)

  // The preview renders the saved version, so it refreshes whenever the version loads or a save changes its row version.
  useEffect(() => {
    runPreview(version.id)
  }, [runPreview, version.id, version.rowVersion])

  const insertVariable = (name: string) => {
    const target = focused.current
    const token = `{{ ${name} }}`
    if (!target) return
    const current = String(getValues(target.name) ?? '')
    const start = target.element.selectionStart ?? current.length
    const end = target.element.selectionEnd ?? current.length
    setValue(target.name, `${current.slice(0, start)}${token}${current.slice(end)}`, {
      shouldDirty: true,
      shouldValidate: true,
    })
  }

  const save = handleSubmit((values) => {
    setFailure(null)
    update.mutate(
      { versionId: version.id, rowVersion: version.rowVersion, content: toInput(channel, values) },
      { onError: (err) => setFailure(failureOf(t, 'saveDraft', err)) },
    )
  })

  const act = (kind: 'publish' | 'archive') => {
    setFailure(null)
    const mutation = kind === 'publish' ? publish : archive
    mutation.mutate(
      { versionId: version.id, rowVersion: version.rowVersion },
      {
        onSuccess: () => {
          setConfirm(null)
          setNotice(
            t(kind === 'publish' ? 'editor.notices.published' : 'editor.notices.archived', {
              number: format.number(version.versionNumber, PLAIN_NUMBER),
            }),
          )
        },
        onError: (err) => {
          setConfirm(null)
          setFailure(failureOf(t, kind === 'publish' ? 'publish' : 'archive', err))
        },
      },
    )
  }

  const test = () => {
    setFailure(null)
    sendTest.mutate(version.id, {
      onSuccess: (result) => setNotice(t('editor.notices.testSent', { address: result.sentTo })),
      onError: (err) => setFailure(failureOf(t, 'test', err)),
    })
  }

  const busy = update.isPending || publish.isPending || archive.isPending
  const field = (
    name: FieldName,
    label: string,
    opts: { multiline?: boolean; helper?: string } = {},
  ) => (
    <Controller
      key={name}
      name={name}
      control={control}
      render={({ field: f, fieldState }) => (
        <TextField
          {...f}
          label={label}
          size="small"
          fullWidth
          multiline={opts.multiline}
          minRows={opts.multiline ? 2 : undefined}
          error={!!fieldState.error}
          helperText={fieldState.error?.message ?? opts.helper}
          slotProps={{ input: { readOnly: !editable }, htmlInput: { dir: 'auto' } }}
          onFocus={(e) => {
            focused.current = { name, element: e.target as HTMLInputElement | HTMLTextAreaElement }
          }}
        />
      )}
    />
  )

  return (
    <Stack spacing={2}>
      {failure && (
        <Alert
          severity="error"
          action={
            failure.conflict ? (
              <Button color="inherit" size="small" onClick={onReload}>
                {t('actions.reload')}
              </Button>
            ) : undefined
          }
        >
          {failure.message}
        </Alert>
      )}
      {!editable && (
        <Alert severity="info">
          {version.status === 'Draft'
            ? t('editor.viewOnlyDraft')
            : t(
                version.status === 'Published'
                  ? 'editor.notEditablePublished'
                  : 'editor.notEditableArchived',
              )}
        </Alert>
      )}

      {editable && (
        <Box>
          <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 0.5 }}>
            {t('editor.variablesHint')}
          </Typography>
          <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 0.75 }}>
            {template.declaredVariables.map((v) => (
              <Tooltip key={v.name} title={t('editor.variableSample', { sample: v.sample })}>
                <Chip
                  size="small"
                  variant={v.isStandard ? 'outlined' : 'filled'}
                  label={<bdi dir="ltr">{v.name}</bdi>}
                  clickable
                  aria-label={t('editor.insertAria', { name: v.name })}
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => insertVariable(v.name)}
                />
              </Tooltip>
            ))}
          </Stack>
        </Box>
      )}

      <Stack
        spacing={2}
        component="form"
        noValidate
        onSubmit={(e) => void save(e)}
        aria-label={t('editor.formAria')}
      >
        {isEmail ? (
          <>
            {field('subject', t('editor.fields.subject'))}
            {field('preheader', t('editor.fields.preheader'), {
              helper: t('editor.fields.preheaderHelp'),
            })}
            {field('greeting', t('editor.fields.greeting'))}
            {field('heading', t('editor.fields.heading'))}
            {paragraphs.fields.map((p, index) => (
              <Stack key={p.id} direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
                {field(`paragraphs.${index}.text`, t('editor.fields.paragraph', { number: index + 1 }), {
                  multiline: true,
                })}
                {editable && paragraphs.fields.length > 1 && (
                  <Button
                    size="small"
                    onClick={() => paragraphs.remove(index)}
                    aria-label={t('editor.fields.removeAria', { number: index + 1 })}
                  >
                    {t('editor.fields.remove')}
                  </Button>
                )}
              </Stack>
            ))}
            {editable && paragraphs.fields.length < 10 && (
              <Box>
                <Button size="small" onClick={() => paragraphs.append({ text: '' })}>
                  {t('editor.fields.addParagraph')}
                </Button>
              </Box>
            )}
            {formState.errors.paragraphs?.root?.message && (
              <Alert severity="error">{formState.errors.paragraphs.root.message}</Alert>
            )}
            {field('actionLabel', t('editor.fields.buttonLabel'), {
              helper: t('editor.fields.buttonLabelHelp'),
            })}
            {field('safetyNote', t('editor.fields.safetyNote'), { multiline: true })}
            {field('footerNote', t('editor.fields.footerNote'), { multiline: true })}
          </>
        ) : (
          <>
            {field('title', t('editor.fields.title'))}
            {field('message', t('editor.fields.message'), { multiline: true })}
            {field('actionLabel', t('editor.fields.actionLabel'))}
          </>
        )}
      </Stack>

      <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
        {editable && (
          <Button
            variant="contained"
            onClick={() => void save()}
            disabled={busy || !formState.isDirty}
          >
            {update.isPending ? t('editor.buttons.saving') : t('editor.buttons.saveDraft')}
          </Button>
        )}
        {canManage && isEmail && (
          <Button variant="outlined" onClick={test} disabled={sendTest.isPending}>
            {sendTest.isPending ? t('editor.buttons.sending') : t('editor.buttons.sendTest')}
          </Button>
        )}
        {editable && (
          <Button
            variant="contained"
            color="success"
            onClick={() => setConfirm('publish')}
            disabled={busy || formState.isDirty}
          >
            {t('editor.buttons.publish')}
          </Button>
        )}
        {canManage && version.status !== 'Archived' && (
          <Button
            variant="outlined"
            color="warning"
            onClick={() => setConfirm('archive')}
            disabled={busy}
          >
            {t('editor.buttons.archive')}
          </Button>
        )}
      </Stack>
      {editable && formState.isDirty && (
        <Typography variant="caption" color="text.secondary">
          {t('editor.saveHint')}
        </Typography>
      )}

      <PreviewPanel preview={preview.data} isPending={preview.isPending} error={preview.error} />

      <Dialog
        open={confirm !== null}
        onClose={busy ? undefined : () => setConfirm(null)}
        aria-labelledby="template-confirm-title"
      >
        <DialogTitle id="template-confirm-title">
          {t(confirm === 'publish' ? 'editor.confirm.publishTitle' : 'editor.confirm.archiveTitle', {
            number: format.number(version.versionNumber, PLAIN_NUMBER),
          })}
        </DialogTitle>
        <DialogContent>
          <DialogContentText>
            {confirm === 'publish'
              ? t('editor.confirm.publishBody')
              : version.status === 'Published'
                ? t('editor.confirm.archivePublishedBody')
                : t('editor.confirm.archiveDraftBody')}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirm(null)} disabled={busy}>
            {t('editor.buttons.cancel')}
          </Button>
          <Button
            variant="contained"
            color={confirm === 'publish' ? 'success' : 'warning'}
            onClick={() => confirm && act(confirm)}
            disabled={busy}
          >
            {confirm === 'publish' ? t('editor.buttons.publish') : t('editor.buttons.archive')}
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar
        open={notice !== null}
        autoHideDuration={8000}
        onClose={() => setNotice(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          severity="success"
          variant="filled"
          closeText={t('actions.close')}
          onClose={() => setNotice(null)}
        >
          {notice}
        </Alert>
      </Snackbar>
    </Stack>
  )
}

function TemplateEditorBody({ templateId }: { templateId: string }) {
  const t = useT('admin.notifications')
  const format = useFormat()
  const canManage = useCanManageNotifications()
  const template = useNotificationTemplate(templateId)
  const [chosen, setChosen] = useState<string | null>(null)
  const detail = template.data

  // The newest draft if there is one, else the live version, else the newest: what an editor most likely came to work on.
  const defaultVersionId =
    detail?.versions.find((v) => v.status === 'Draft')?.id ??
    detail?.versions.find((v) => v.status === 'Published')?.id ??
    detail?.versions[0]?.id ??
    null
  const versionId =
    chosen !== null && detail?.versions.some((v) => v.id === chosen) ? chosen : defaultVersionId
  const version = useTemplateVersion(templateId, versionId)
  const createDraft = useCreateTemplateDraft(templateId)
  const [createFailure, setCreateFailure] = useState<string | null>(null)

  const reload = () => {
    void template.refetch()
    void version.refetch()
  }

  const newVersion = () => {
    setCreateFailure(null)
    createDraft.mutate(versionId ? { copyFromVersionId: versionId } : {}, {
      onSuccess: (created) => setChosen(created.id),
      onError: (err) =>
        setCreateFailure(t('editor.createFailed', { detail: errorText(t, err) })),
    })
  }

  return (
    <>
      {template.isError && (
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => void template.refetch()}>
              {t('actions.retry')}
            </Button>
          }
        >
          {t('editor.loadFailed', { detail: errorText(t, template.error) })}
        </Alert>
      )}
      {template.isLoading && <CircularProgress size={24} aria-label={t('editor.loading')} />}

      {detail && (
        <Stack
          direction={{ xs: 'column', md: 'row' }}
          spacing={2}
          sx={{ alignItems: 'flex-start' }}
        >
          <Paper elevation={1} sx={{ p: 1, width: { xs: '100%', md: 260 }, flexShrink: 0 }}>
            <Typography variant="overline" sx={{ px: 1 }}>
              {t('editor.versions')}
            </Typography>
            <List dense aria-label={t('editor.versions')}>
              {detail.versions.map((v) => (
                <ListItem key={v.id} disablePadding>
                  <ListItemButton selected={v.id === versionId} onClick={() => setChosen(v.id)}>
                    <ListItemText
                      primary={
                        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                          <span>{t('editor.versionN', { number: format.number(v.versionNumber, PLAIN_NUMBER) })}</span>
                          <Chip
                            size="small"
                            color={STATUS_COLOR[v.status]}
                            label={t(`editor.versionStatuses.${v.status}`)}
                          />
                        </Stack>
                      }
                      secondary={`${format.date(v.createdAtUtc, DATE_ONLY)}${v.createdBy ? ` · ${v.createdBy}` : ''}`}
                    />
                  </ListItemButton>
                </ListItem>
              ))}
            </List>
            {canManage && (
              <Box sx={{ p: 1 }}>
                <Button
                  size="small"
                  variant="outlined"
                  onClick={newVersion}
                  disabled={createDraft.isPending}
                >
                  {createDraft.isPending ? t('editor.creating') : t('editor.newVersion')}
                </Button>
              </Box>
            )}
            {createFailure && (
              <Alert severity="error" sx={{ m: 1 }}>
                {createFailure}
              </Alert>
            )}
          </Paper>

          <Paper elevation={1} sx={{ p: 2, flex: 1, minWidth: 0, width: '100%' }}>
            {version.isError && (
              <Alert
                severity="error"
                action={
                  <Button color="inherit" size="small" onClick={() => void version.refetch()}>
                    {t('actions.retry')}
                  </Button>
                }
              >
                {t('editor.versionLoadFailed', { detail: errorText(t, version.error) })}
              </Alert>
            )}
            {version.isLoading && <CircularProgress size={24} aria-label={t('editor.versionLoading')} />}
            {version.data && (
              <VersionEditor
                key={`${version.data.id}:${version.data.rowVersion}`}
                template={detail}
                version={version.data}
                canManage={canManage}
                onReload={reload}
              />
            )}
          </Paper>
        </Stack>
      )}
    </>
  )
}

/**
 * specs/067 US7 — one template: its versions, the draft editor, a sandboxed preview, a test send to yourself, and publish and archive
 * with confirmations. Every change sends the version's row version as `If-Match`, so a stale edit is a visible conflict, never an
 * overwrite.
 */
export function AdminNotificationTemplateEditorPage() {
  const { templateId = '' } = useParams()
  const t = useOuterT('admin.notifications')
  const detail = useNotificationTemplate(templateId).data

  return (
    <AdminShell
      title={detail?.name ?? t('editor.fallbackTitle')}
      subtitle={
        detail
          ? t('editor.subtitle', {
              type: detail.type,
              channel: channelLabel(t, detail.channel),
              language: detail.language,
            })
          : undefined
      }
      actions={
        <Button component={RouterLink} to="/admin/notifications/templates" size="small">
          {t('editor.allTemplates')}
        </Button>
      }
    >
      <TemplateEditorBody templateId={templateId} />
    </AdminShell>
  )
}
