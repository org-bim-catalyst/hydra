import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { LanguageContext } from './languageContext'
import { createT, useT } from './useT'

afterEach(() => vi.restoreAllMocks())

describe('createT', () => {
  it('resolves English plurals with Intl.PluralRules', () => {
    const t = createT('notifications', 'en')
    expect(t('bell.labelUnread', { count: 3 })).toBe('Notifications, 3 unread')
  })

  it('resolves all six Arabic plural forms', () => {
    const t = createT('notifications', 'ar')
    const forms = [0, 1, 2, 3, 11, 100].map((count) => t('bell.labelUnread', { count }))
    expect(new Set(forms).size).toBe(6)
    expect(forms[1]).toContain('واحد')
    expect(forms[3]).toContain('3')
  })

  it('inserts params as plain text, never as markup', () => {
    const t = createT('notifications', 'en')
    const text = t('preferences.switchLabel', { category: '<b>x</b>', channel: 'Email' })
    expect(text).toBe('<b>x</b> notifications by Email')
  })

  it('isolates string params in right-to-left text so a Latin name does not reorder the punctuation', () => {
    const t = createT('notifications', 'ar')
    expect(t('preferences.switchLabel', { category: 'OpenAI', channel: 'SMTP' })).toContain(
      '⁨OpenAI⁩',
    )
  })

  it('falls back to English and reports, in development, when a key is missing', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {})
    const t = createT('notifications', 'ar') as unknown as (key: string) => string
    expect(t('nope.missing')).toBe('nope.missing')
    expect(spy).toHaveBeenCalledOnce()
  })
})

describe('useT', () => {
  function Probe() {
    const t = useT('common')
    return <p>{t('actions.retry')}</p>
  }

  it('is English without a surface above it', () => {
    render(<Probe />)
    expect(screen.getByText('Retry')).toBeInTheDocument()
  })

  it('follows the surrounding language', () => {
    render(
      <LanguageContext.Provider value={{ language: 'ar', direction: 'rtl', active: true }}>
        <Probe />
      </LanguageContext.Provider>,
    )
    expect(screen.getByText('إعادة المحاولة')).toBeInTheDocument()
  })
})
