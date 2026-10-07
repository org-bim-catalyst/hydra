import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import { createT } from '../../../i18n/useT'
import type {
  NotificationTemplateDetail,
  TemplateVersion,
} from '../api/adminNotificationTemplatesApi'
import { templateTextProblem } from '../forms/templateText'
import { AdminNotificationTemplateEditorPage } from './AdminNotificationTemplateEditorPage'

expect.extend(toHaveNoViolations)

const plain = (text: string) => text.replace(/[⁦-⁩]/g, '')
const hasText = (expected: string) => (_: string, element: Element | null) =>
  element !== null && element.children.length === 0 && plain(element.textContent ?? '') === expected

const detail: NotificationTemplateDetail = {
  templateId: 't1',
  type: 'workflow.execution.failed',
  category: 'Workflow',
  channel: 'Email',
  language: 'ar',
  name: 'Workflow failed (email)',
  publishedVersionId: 'v1',
  isShippedDefault: true,
  versions: [
    {
      id: 'v2',
      versionNumber: 2,
      status: 'Draft',
      createdAtUtc: '2026-10-05T00:00:00Z',
      createdBy: 'Ada Lovelace',
      publishedAtUtc: null,
      archivedAtUtc: null,
    },
    {
      id: 'v1',
      versionNumber: 1,
      status: 'Published',
      createdAtUtc: '2026-10-01T00:00:00Z',
      createdBy: 'System',
      publishedAtUtc: '2026-10-01T00:00:00Z',
      archivedAtUtc: null,
    },
  ],
  declaredVariables: [
    { name: 'workflowName', sample: 'your workflow', fallback: 'your workflow', isStandard: false },
    { name: 'recipientDisplayName', sample: 'there', fallback: 'there', isStandard: true },
  ],
}

const version = (
  id: string,
  number: number,
  status: TemplateVersion['status'],
  rowVersion: string,
): TemplateVersion => ({
  id,
  templateId: 't1',
  versionNumber: number,
  status,
  rowVersion,
  subject: '{{ workflowName }} failed',
  preheader: null,
  greeting: 'Hello {{ recipientDisplayName }}',
  heading: 'It failed',
  bodyParagraphs: ['Reason: {{ workflowName }}'],
  actionLabel: 'Open',
  safetyNote: 'Ignore this if it was not you.',
  footerNote: null,
  title: null,
  message: null,
  usedVariables: ['workflowName'],
  createdAtUtc: '2026-10-05T00:00:00Z',
  publishedAtUtc: null,
  archivedAtUtc: null,
})

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.notifications.view', 'admin.notifications.manage'],
    }),
  ),
  http.get('*/api/v1/admin/notifications/templates/t1', () => HttpResponse.json(detail)),
  http.get('*/api/v1/admin/notifications/templates/t1/versions/v2', () =>
    HttpResponse.json(version('v2', 2, 'Draft', 'cm93LXYy')),
  ),
  http.get('*/api/v1/admin/notifications/templates/t1/versions/v1', () =>
    HttpResponse.json(version('v1', 1, 'Published', 'cm93LXYx')),
  ),
  http.post('*/api/v1/admin/notifications/templates/t1/versions/:versionId/actions/preview', () =>
    HttpResponse.json({
      subject: 'your workflow failed',
      html: '<html><body><h1>RENDERED HEADING</h1><script>alert(1)</script></body></html>',
      text: 'text',
      title: null,
      message: null,
      actionLabel: null,
      language: 'ar',
      direction: 'rtl',
    }),
  ),
  http.post('*/api/v1/admin/notifications/templates/t1/versions/v2/actions/publish', () =>
    HttpResponse.json(version('v2', 2, 'Published', 'cm93LXYz')),
  ),
  http.post('*/api/v1/admin/notifications/templates/t1/versions/v2/actions/send-test', () =>
    HttpResponse.json({ sentTo: 'a•••@example.com' }, { status: 202 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  seedArabic(queryClient)
  const router = createMemoryRouter(
    [
      {
        path: '/admin/notifications/templates/:templateId',
        element: (
          <LocalizedSurface scope="page">
            <AdminNotificationTemplateEditorPage />
          </LocalizedSurface>
        ),
      },
    ],
    { initialEntries: ['/admin/notifications/templates/t1'] },
  )
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

/** Open MUI dialogs break role queries in jsdom, so buttons are found by their text. */
const button = (text: string) => screen.getByText(text).closest('button') as HTMLButtonElement

describe('AdminNotificationTemplateEditorPage in Arabic (T218)', () => {
  it('renders in ar/rtl with Arabic labels; template content and variable names are never translated', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    // The template's own wording (and its {{ tokens }}) is data, whatever the interface language is.
    expect(await screen.findByLabelText('الموضوع')).toHaveValue('{{ workflowName }} failed')
    expect(screen.getByLabelText('التحية')).toHaveValue('Hello {{ recipientDisplayName }}')
    expect(screen.getByLabelText('الفقرة 1')).toHaveValue('Reason: {{ workflowName }}')
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('Workflow failed (email)')).toBeInTheDocument()
    expect(
      screen.getByText(hasText('workflow.execution.failed · البريد الإلكتروني · ar')),
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'كل القوالب' })).toBeInTheDocument()

    const versions = within(screen.getByRole('list', { name: 'الإصدارات' }))
    expect(versions.getByText(hasText('الإصدار 2'))).toBeInTheDocument()
    expect(versions.getByText('مسودة')).toBeInTheDocument()
    expect(versions.getByText('منشور')).toBeInTheDocument()
    expect(screen.getByText('المتغيرات: انقر على حقل ثم على متغير لإدراجه.')).toBeInTheDocument()
    for (const name of ['workflowName', 'recipientDisplayName']) {
      const chip = screen.getByRole('button', { name: `إدراج ⁨${name}⁩` })
      // The variable name inside the chip is the identifier itself.
      expect(within(chip).getByText(name)).toHaveAttribute('dir', 'ltr')
    }
    expect(screen.getByRole('button', { name: 'حفظ المسودة' })).toBeDisabled()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('keeps the sandboxed preview frame sandboxed with its server-rendered srcdoc, titled in Arabic', async () => {
    renderPage()

    const frame = (await screen.findByTitle('معاينة البريد الإلكتروني')) as HTMLIFrameElement
    expect(frame.tagName).toBe('IFRAME')
    expect(frame.getAttribute('sandbox')).toBe('')
    expect(frame.getAttribute('srcdoc')).toContain('RENDERED HEADING')
    expect(screen.queryByText('RENDERED HEADING')).not.toBeInTheDocument()
    expect(document.querySelector('script')).toBeNull()
    expect(screen.getByText('معاينة ببيانات تجريبية')).toBeInTheDocument()
    expect(screen.getByText('الموضوع:')).toBeInTheDocument()
  })

  it('shows client-side validation messages in Arabic, keeping variable names verbatim', async () => {
    renderPage()
    const subject = await screen.findByLabelText('الموضوع')

    fireEvent.change(subject, { target: { value: 'See https://example.com' } })
    expect(
      await screen.findByText('الروابط وHTML غير مسموح بها. تأتي الروابط من زر الإجراء فقط.'),
    ).toBeInTheDocument()

    fireEvent.change(subject, { target: { value: '{{ nope }} failed' } })
    expect(
      await screen.findByText(
        (_, element) =>
          element?.tagName === 'P' &&
          plain(element.textContent ?? '') === 'العنصر {{ nope }} ليس متغيرًا يوفره هذا الإشعار.',
      ),
    ).toBeInTheDocument()

    fireEvent.change(subject, { target: { value: '' } })
    expect(await screen.findByText('هذا الحقل مطلوب.')).toBeInTheDocument()
  })

  it('translates every templateTextProblem message and leaves English as it was', () => {
    const ar = createT('admin.notifications', 'ar')
    const allowed = new Set(['name'])
    expect(templateTextProblem('x'.repeat(5), 3, allowed)).toBe('Can be at most 3 characters.')
    expect(templateTextProblem('x'.repeat(5), 3, allowed, ar)).toBe(
      'يجب ألا يزيد على ⁨3⁩ حرفًا.'.replace(/[⁨⁩]/g, ''),
    )
    expect(templateTextProblem('{{ actionUrl }}', 50, allowed, ar)).toBe(
      'تضيف المنصة رابط الإجراء ولا يمكن وضعه في النص.',
    )
    expect(templateTextProblem('{{ 1x }}', 50, allowed, ar)).toMatch(/ليس متغيرًا صالحًا/)
    expect(templateTextProblem('oops }}', 50, allowed, ar)).toMatch(/غير مطابق/)
    expect(templateTextProblem('{{ name }}', 50, allowed, ar)).toBeNull()
  })

  it('confirms publishing in an Arabic, rtl dialog and announces the result in Arabic', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()
    await screen.findByLabelText('الموضوع')

    fireEvent.click(button('نشر'))

    expect(await screen.findByText(hasText('نشر الإصدار 2؟'))).toBeInTheDocument()
    expect(screen.getByText(/تستخدم الإشعارات الجديدة هذه الصياغة فورًا/)).toBeInTheDocument()
    expect(document.querySelector('.MuiDialog-root')).toHaveAttribute('dir', 'rtl')

    const dialog = document.querySelector('.MuiDialog-root') as HTMLElement
    fireEvent.click(within(dialog).getByText('نشر').closest('button') as HTMLButtonElement)

    expect(await screen.findByText(hasText('الإصدار 2 مباشر الآن.'))).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('sends the test email and says so in Arabic, with the masked address as returned', async () => {
    renderPage()
    await screen.findByLabelText('الموضوع')

    fireEvent.click(button('إرسال تجربة إليّ'))

    await waitFor(() =>
      expect(
        screen.getByText(hasText('أُرسل بريد تجريبي إلى a•••@example.com.')),
      ).toBeInTheDocument(),
    )
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await screen.findByLabelText('الموضوع')
      await screen.findByTitle('معاينة البريد الإلكتروني')
      // The preview frame holds a rendered email from the server, a separate document; axe checks this page, not the frame.
      expect(await axe(container, { iframes: false })).toHaveNoViolations()
    })
  }
})
