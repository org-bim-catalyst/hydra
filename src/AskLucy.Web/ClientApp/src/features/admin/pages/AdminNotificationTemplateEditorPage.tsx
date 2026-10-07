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

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.'

const STATUS_COLOR: Record<TemplateVersionStatus, 'default' | 'info' | 'success'> = {
  Draft: 'info',
  Published: 'success',
  Archived: 'default',
}

function text(maxLength: number, allowed: ReadonlySet<string>, required: boolean) {
  return z.string().superRefine((value, ctx) => {
    if (required && value.trim() === '') {
      ctx.addIssue({ code: 'custom', message: 'This field is required.' })
      return
    }
    const problem = templateTextProblem(value, maxLength, allowed)
    if (problem) ctx.addIssue({ code: 'custom', message: problem })
  })
}

function buildSchema(channel: 'Email' | 'InApp', allowed: ReadonlySet<string>) {
  const email = channel === 'Email'
  return z.object({
    subject: email ? text(200, allowed, true) : z.string(),
    preheader: email ? text(200, allowed, false) : z.string(),
    greeting: email ? text(200, allowed, false) : z.string(),
    heading: email ? text(200, allowed, true) : z.string(),
    paragraphs: z
      .array(z.object({ text: email ? text(1000, allowed, true) : z.string() }))
      .refine((p) => !email || (p.length >= 1 && p.length <= 10), {
        message: 'An email needs between 1 and 10 paragraphs.',
      }),
    safetyNote: email ? text(500, allowed, true) : z.string(),
    footerNote: email ? text(500, allowed, false) : z.string(),
    title: email ? z.string() : text(200, allowed, true),
    message: email ? z.string() : text(1000, allowed, true),
    actionLabel: text(60, allowed, false),
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

const failureOf = (what: string, err: unknown): ActionFailure => ({
  message: `${what} ${errorMessage(err)}`,
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
  return (
    <Paper variant="outlined" sx={{ p: 2, minHeight: 160 }}>
      <Typography variant="subtitle2" gutterBottom>
        Preview with sample data
      </Typography>
      {isPending && <CircularProgress size={20} aria-label="Rendering the preview" />}
      {error !== null && error !== undefined && (
        <Alert severity="error">{`The preview couldn't be rendered. ${errorMessage(error)}`}</Alert>
      )}
      {preview?.html != null && (
        <Stack spacing={1}>
          <Typography variant="body2">
            <strong>Subject:</strong> {preview.subject}
          </Typography>
          {/* The rendered email is shown in a frame with every sandbox permission off: no scripts, no navigation, no same-origin access. */}
          <Box
            component="iframe"
            title="Email preview"
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
  const channel = template.channel
  const isEmail = channel === 'Email'
  const editable = canManage && version.status === 'Draft'
  const allowed = useMemo(
    () => new Set(template.declaredVariables.map((v) => v.name)),
    [template.declaredVariables],
  )
  const schema = useMemo(() => buildSchema(channel, allowed), [channel, allowed])

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
      { onError: (err) => setFailure(failureOf("The draft wasn't saved.", err)) },
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
            kind === 'publish'
              ? `Version ${version.versionNumber} is now live.`
              : `Version ${version.versionNumber} was archived.`,
          )
        },
        onError: (err) => {
          setConfirm(null)
          setFailure(
            failureOf(
              kind === 'publish' ? "The version wasn't published." : "The version wasn't archived.",
              err,
            ),
          )
        },
      },
    )
  }

  const test = () => {
    setFailure(null)
    sendTest.mutate(version.id, {
      onSuccess: (result) => setNotice(`A test email was sent to ${result.sentTo}.`),
      onError: (err) => setFailure(failureOf("The test email wasn't sent.", err)),
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
          slotProps={{ input: { readOnly: !editable } }}
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
                Reload
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
            ? 'You can view this draft but not change it.'
            : `A ${version.status.toLowerCase()} version can't be edited. Create a new version to change the wording.`}
        </Alert>
      )}

      {editable && (
        <Box>
          <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 0.5 }}>
            Variables: click a field, then a variable to insert it.
          </Typography>
          <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 0.75 }}>
            {template.declaredVariables.map((v) => (
              <Tooltip key={v.name} title={`Sample: ${v.sample}`}>
                <Chip
                  size="small"
                  variant={v.isStandard ? 'outlined' : 'filled'}
                  label={v.name}
                  clickable
                  aria-label={`Insert ${v.name}`}
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
        aria-label="Template version"
      >
        {isEmail ? (
          <>
            {field('subject', 'Subject')}
            {field('preheader', 'Preheader', {
              helper: 'The short line shown after the subject in an inbox.',
            })}
            {field('greeting', 'Greeting')}
            {field('heading', 'Heading')}
            {paragraphs.fields.map((p, index) => (
              <Stack key={p.id} direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
                {field(`paragraphs.${index}.text`, `Paragraph ${index + 1}`, { multiline: true })}
                {editable && paragraphs.fields.length > 1 && (
                  <Button
                    size="small"
                    onClick={() => paragraphs.remove(index)}
                    aria-label={`Remove paragraph ${index + 1}`}
                  >
                    Remove
                  </Button>
                )}
              </Stack>
            ))}
            {editable && paragraphs.fields.length < 10 && (
              <Box>
                <Button size="small" onClick={() => paragraphs.append({ text: '' })}>
                  Add paragraph
                </Button>
              </Box>
            )}
            {formState.errors.paragraphs?.root?.message && (
              <Alert severity="error">{formState.errors.paragraphs.root.message}</Alert>
            )}
            {field('actionLabel', 'Button label', {
              helper:
                'The button links to the notification; the link itself is added by the platform.',
            })}
            {field('safetyNote', 'Safety note', { multiline: true })}
            {field('footerNote', 'Footer note', { multiline: true })}
          </>
        ) : (
          <>
            {field('title', 'Title')}
            {field('message', 'Message', { multiline: true })}
            {field('actionLabel', 'Action label')}
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
            {update.isPending ? 'Saving…' : 'Save draft'}
          </Button>
        )}
        {canManage && isEmail && (
          <Button variant="outlined" onClick={test} disabled={sendTest.isPending}>
            {sendTest.isPending ? 'Sending…' : 'Send test to me'}
          </Button>
        )}
        {editable && (
          <Button
            variant="contained"
            color="success"
            onClick={() => setConfirm('publish')}
            disabled={busy || formState.isDirty}
          >
            Publish
          </Button>
        )}
        {canManage && version.status !== 'Archived' && (
          <Button
            variant="outlined"
            color="warning"
            onClick={() => setConfirm('archive')}
            disabled={busy}
          >
            Archive
          </Button>
        )}
      </Stack>
      {editable && formState.isDirty && (
        <Typography variant="caption" color="text.secondary">
          Save the draft to refresh the preview and to publish it.
        </Typography>
      )}

      <PreviewPanel preview={preview.data} isPending={preview.isPending} error={preview.error} />

      <Dialog
        open={confirm !== null}
        onClose={busy ? undefined : () => setConfirm(null)}
        aria-labelledby="template-confirm-title"
      >
        <DialogTitle id="template-confirm-title">
          {confirm === 'publish'
            ? `Publish version ${version.versionNumber}?`
            : `Archive version ${version.versionNumber}?`}
        </DialogTitle>
        <DialogContent>
          <DialogContentText>
            {confirm === 'publish'
              ? `New notifications use this wording straight away. The version it replaces is archived and kept${template.isShippedDefault ? '' : ''}. A published version can't be edited.`
              : version.status === 'Published'
                ? 'This is the live version. Archiving it leaves the template without one, and notifications of this type will fall back to English or fail to render.'
                : 'An archived draft stays in the history but can no longer be published.'}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirm(null)} disabled={busy}>
            Cancel
          </Button>
          <Button
            variant="contained"
            color={confirm === 'publish' ? 'success' : 'warning'}
            onClick={() => confirm && act(confirm)}
            disabled={busy}
          >
            {confirm === 'publish' ? 'Publish' : 'Archive'}
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar
        open={notice !== null}
        autoHideDuration={8000}
        onClose={() => setNotice(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity="success" variant="filled" onClose={() => setNotice(null)}>
          {notice}
        </Alert>
      </Snackbar>
    </Stack>
  )
}

/**
 * specs/067 US7 — one template: its versions, the draft editor, a sandboxed preview, a test send to yourself, and publish and archive
 * with confirmations. Every change sends the version's row version as `If-Match`, so a stale edit is a visible conflict, never an
 * overwrite.
 */
export function AdminNotificationTemplateEditorPage() {
  const { templateId = '' } = useParams()
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
      onError: (err) => setCreateFailure(`The new version wasn't created. ${errorMessage(err)}`),
    })
  }

  return (
    <AdminShell
      title={detail?.name ?? 'Notification template'}
      subtitle={
        detail
          ? `${detail.type} · ${detail.channel === 'Email' ? 'Email' : 'In-app'} · ${detail.language}`
          : undefined
      }
      actions={
        <Button component={RouterLink} to="/admin/notifications/templates" size="small">
          All templates
        </Button>
      }
    >
      {template.isError && (
        <Alert
          severity="error"
          action={
            <Button color="inherit" size="small" onClick={() => void template.refetch()}>
              Retry
            </Button>
          }
        >
          {`The template couldn't be loaded. ${errorMessage(template.error)}`}
        </Alert>
      )}
      {template.isLoading && <CircularProgress size={24} aria-label="Loading the template" />}

      {detail && (
        <Stack
          direction={{ xs: 'column', md: 'row' }}
          spacing={2}
          sx={{ alignItems: 'flex-start' }}
        >
          <Paper elevation={1} sx={{ p: 1, width: { xs: '100%', md: 260 }, flexShrink: 0 }}>
            <Typography variant="overline" sx={{ px: 1 }}>
              Versions
            </Typography>
            <List dense aria-label="Versions">
              {detail.versions.map((v) => (
                <ListItem key={v.id} disablePadding>
                  <ListItemButton selected={v.id === versionId} onClick={() => setChosen(v.id)}>
                    <ListItemText
                      primary={
                        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                          <span>{`Version ${v.versionNumber}`}</span>
                          <Chip size="small" color={STATUS_COLOR[v.status]} label={v.status} />
                        </Stack>
                      }
                      secondary={`${new Date(v.createdAtUtc).toLocaleDateString()}${v.createdBy ? ` · ${v.createdBy}` : ''}`}
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
                  {createDraft.isPending ? 'Creating…' : 'New version'}
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
                    Retry
                  </Button>
                }
              >
                {`The version couldn't be loaded. ${errorMessage(version.error)}`}
              </Alert>
            )}
            {version.isLoading && <CircularProgress size={24} aria-label="Loading the version" />}
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
    </AdminShell>
  )
}
