import { describe, expect, it, vi } from 'vitest'
import { actionAllowlist, validateAction } from './allowlist'

describe('action allowlist (contracts/action-allowlist.md)', () => {
  it('validates each listed command with correct arguments', () => {
    expect(validateAction('select', { layerId: 'l1', elementId: 'e1' }).valid).toBe(true)
    expect(validateAction('clearSelection', {}).valid).toBe(true)
    expect(validateAction('zoomToLocation', { latitude: 25.1, longitude: 55.2 }).valid).toBe(true)
    expect(validateAction('setLayerVisibility', { layerId: 'l1', visible: true }).valid).toBe(true)
    expect(validateAction('setViewMode', { mode: 'isometric' }).valid).toBe(true)
    expect(validateAction('setMapStyle', { mapStyle: 'satellite' }).valid).toBe(true)
  })

  it('rejects a command not on the allowlist', () => {
    const result = validateAction('doAnythingElse', {})
    expect(result.valid).toBe(false)
  })

  it('rejects malformed arguments for a listed command', () => {
    expect(validateAction('zoomToLocation', { latitude: 999, longitude: 55.2 }).valid).toBe(false)
    expect(validateAction('select', { layerId: 'l1' }).valid).toBe(false)
    expect(validateAction('setViewMode', { mode: 'not-a-real-mode' }).valid).toBe(false)
  })

  it('confirms the operations that mutate viewer content are absent and unreachable', () => {
    expect(actionAllowlist.addLayer).toBeUndefined()
    expect(actionAllowlist.removeLayer).toBeUndefined()
    expect(actionAllowlist.displayContent).toBeUndefined()
    expect(actionAllowlist.createOverlay).toBeUndefined()
    // specs/051 T048/contracts/action-allowlist-extension.md "Deliberately still excluded"
    expect(actionAllowlist.loadContent).toBeUndefined()
    expect(actionAllowlist.replaceContent).toBeUndefined()
    expect(actionAllowlist.unloadContent).toBeUndefined()
  })

  it('T048 (specs/051): validates and invokes selectAndFrame correctly', () => {
    expect(validateAction('selectAndFrame', { layerId: 'l1', elementId: 'e1' }).valid).toBe(true)
    expect(validateAction('selectAndFrame', { layerId: 'l1' }).valid).toBe(false)
  })

  it('records every rejection for diagnosis (spec FR-014) — a disallowed command and malformed arguments both', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined)

    validateAction('removeLayer', { layerId: 'x' })
    validateAction('zoomToLocation', { latitude: 999, longitude: 0 })

    expect(warnSpy).toHaveBeenCalledTimes(2)
    expect(warnSpy.mock.calls[0][0]).toContain('removeLayer')
    expect(warnSpy.mock.calls[1][0]).toContain('zoomToLocation')

    warnSpy.mockRestore()
  })
})
