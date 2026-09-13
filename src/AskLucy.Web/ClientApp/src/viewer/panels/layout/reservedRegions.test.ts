import { afterEach, describe, expect, it } from 'vitest'
import { RESERVED_ATTRIBUTE, collectReservedRects } from './reservedRegions'

function stubRect(el: Element, rect: { left: number; top: number; width: number; height: number }) {
  const domRect = {
    ...rect,
    right: rect.left + rect.width,
    bottom: rect.top + rect.height,
    x: rect.left,
    y: rect.top,
    toJSON: () => rect,
  } as DOMRect
  el.getBoundingClientRect = () => domRect
}

afterEach(() => {
  document.body.innerHTML = ''
})

describe('collectReservedRects', () => {
  it('C1/C4: returns [] when nothing declares the attribute, without throwing', () => {
    expect(collectReservedRects({ left: 0, top: 0 })).toEqual([])
  })

  it('C2: normalizes viewport-relative rects into host-relative coordinates', () => {
    const el = document.createElement('div')
    el.setAttribute(RESERVED_ATTRIBUTE, '')
    stubRect(el, { left: 120, top: 80, width: 200, height: 40 })
    document.body.appendChild(el)

    const rects = collectReservedRects({ left: 20, top: 10 })
    expect(rects).toEqual([{ x: 100, y: 70, width: 200, height: 40 }])
  })

  it('C3: discards a zero-size (hidden) element rather than reserving the origin', () => {
    const el = document.createElement('div')
    el.setAttribute(RESERVED_ATTRIBUTE, '')
    stubRect(el, { left: 0, top: 0, width: 0, height: 0 })
    document.body.appendChild(el)

    expect(collectReservedRects({ left: 0, top: 0 })).toEqual([])
  })

  it('collects every declaring element, in document order', () => {
    const a = document.createElement('div')
    a.setAttribute(RESERVED_ATTRIBUTE, '')
    stubRect(a, { left: 0, top: 0, width: 50, height: 50 })
    const b = document.createElement('div')
    b.setAttribute(RESERVED_ATTRIBUTE, '')
    stubRect(b, { left: 100, top: 0, width: 50, height: 50 })
    document.body.append(a, b)

    const rects = collectReservedRects({ left: 0, top: 0 })
    expect(rects).toEqual([
      { x: 0, y: 0, width: 50, height: 50 },
      { x: 100, y: 0, width: 50, height: 50 },
    ])
  })

  it('ignores an element with no reserved attribute', () => {
    const el = document.createElement('div')
    stubRect(el, { left: 0, top: 0, width: 100, height: 100 })
    document.body.appendChild(el)
    expect(collectReservedRects({ left: 0, top: 0 })).toEqual([])
  })
})
