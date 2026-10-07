import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { LocalizedSurface } from '../../../../i18n/LocalizedSurface'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../../i18n/testUtils'
import { customModel } from './customModelFixtures'
import { OverwrittenFilesDialog } from './OverwrittenFilesDialog'

expect.extend(toHaveNoViolations)

const files = [
  { relativePath: 'config.json', previousSizeBytes: 812, overwrittenAtUtc: '2026-09-23T10:01:00Z' },
  {
    relativePath: 'onnx/text_encoder.onnx',
    previousSizeBytes: 3 * 2 ** 20,
    overwrittenAtUtc: '2026-09-23T10:02:00Z',
  },
]

const server = setupServer(
  http.get('*/api/v1/admin/custom-models/:id', () =>
    HttpResponse.json({
      ...customModel({ overwrittenFileCount: 3 }),
      overwrittenFiles: { items: files, page: 1, pageSize: 2, totalCount: 3 },
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
        <OverwrittenFilesDialog
          open
          modelId="model-1"
          modelName="supertonic-3"
          onClose={() => undefined}
        />
      </LocalizedSurface>
    </QueryClientProvider>,
  )
}

describe('OverwrittenFilesDialog in Arabic (T215)', () => {
  it('renders in rtl with Arabic headers, left-to-right paths and sizes, and Arabic pagination', async () => {
    const fallbacks = watchI18nFallbacks()
    renderDialog()

    expect(await screen.findByText('config.json')).toHaveAttribute('dir', 'ltr')
    expect(screen.getByText('الملفات التي استبدلها ⁨supertonic-3⁩')).toBeInTheDocument()
    expect(screen.getByText('الملف').closest('[dir]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('الحجم السابق')).toBeInTheDocument()
    expect(screen.getByText('812 بايت')).toHaveAttribute('dir', 'ltr')
    expect(screen.getByText('3 MB')).toHaveAttribute('dir', 'ltr')
    expect(screen.getByText('1–2 من 3')).toBeInTheDocument()
    expect(screen.getByLabelText('الانتقال إلى الصفحة التالية')).toBeInTheDocument()
    expect(screen.getByText('إغلاق')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    renderDialog()
    await screen.findByText('config.json')

    expect(await axe(document.body)).toHaveNoViolations()
  })
})
