import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocalizedSurface } from '../../../../i18n/LocalizedSurface'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../../i18n/testUtils'
import { AddCustomModelDialog } from './AddCustomModelDialog'

expect.extend(toHaveNoViolations)

const SOURCE = 'https://huggingface.co/Supertone/supertonic-3'

const server = setupServer(
  http.get('*/api/v1/admin/custom-models/deployment-status', () =>
    HttpResponse.json({
      isConfigured: true,
      transport: 'FTP',
      maxDeploymentBytes: 2 ** 30,
      allowedDestinationPrefixes: ['Models'],
    }),
  ),
  http.post('*/api/v1/admin/custom-models/source-preview', () =>
    HttpResponse.json({
      isValid: true,
      error: null,
      repositoryId: 'Supertone/supertonic-3',
      revision: 'main',
      filePath: null,
      derivedName: 'supertonic-3',
      nameAvailable: false,
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <LocalizedSurface scope="subtree">
        <AddCustomModelDialog open onClose={vi.fn()} />
      </LocalizedSurface>
    </QueryClientProvider>,
  )
}

// The dialog renders in a portal, so queries run over the whole document and use text, not roles.
describe('AddCustomModelDialog in Arabic (T215)', () => {
  it('renders in rtl with Arabic copy, warnings and helper text, and no catalog fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    renderDialog()

    expect(await screen.findByText('إضافة نموذج مخصص')).toBeInTheDocument()
    expect(screen.getByText('إضافة نموذج مخصص').closest('[dir]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText(/ينزّل الخادم المستودع من Hugging Face/)).toBeInTheDocument()
    expect(
      await screen.findByText(/يُجري هذا الخادم النشر عبر FTP عادي/, {
        selector: '.MuiAlert-message',
      }),
    ).toBeInTheDocument()
    expect(screen.getByText(/نسبةً إلى جذر النشر. يجب أن يبدأ بـ/)).toBeInTheDocument()
    expect(screen.getByLabelText(/^المصدر/)).toHaveAttribute('dir', 'ltr')
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the Arabic validation messages and the name field when the name is taken', async () => {
    renderDialog()
    await waitFor(() => expect(screen.getByText('نشر').closest('button')).toBeEnabled())

    fireEvent.click(screen.getByText('نشر'))
    expect(await screen.findByText('أدخل رابط مستودع Hugging Face.')).toBeInTheDocument()
    expect(screen.getByText('أدخل مجلد الوجهة.')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText(/^المصدر/), { target: { value: SOURCE } })
    expect(
      await screen.findByText('يوجد نموذج باسم ⁨supertonic-3⁩ بالفعل. اختر اسمًا آخر.'),
    ).toBeInTheDocument()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    renderDialog()
    await screen.findByText(/نسبةً إلى جذر النشر. يجب أن يبدأ بـ/)

    expect(await axe(document.body)).toHaveNoViolations()
  })
})
