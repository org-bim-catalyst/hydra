import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/httpClient'
import { shouldRetryQuery } from './queryRetry'

describe('shouldRetryQuery', () => {
  it('never retries a 4xx', () => {
    expect(shouldRetryQuery(0, new ApiError(404, 'Not Found'))).toBe(false)
    expect(shouldRetryQuery(0, new ApiError(429, 'Too Many Requests'))).toBe(false)
  })

  it('retries any other failure twice', () => {
    for (const error of [new ApiError(500, 'Server Error'), new TypeError('Failed to fetch')]) {
      expect(shouldRetryQuery(1, error)).toBe(true)
      expect(shouldRetryQuery(2, error)).toBe(false)
    }
  })

  // Found live 2026-09-25: a deploy's ~16 s host restart outlasted two retries.
  it('keeps retrying a 503 long enough to ride out a host restart', () => {
    const unavailable = new ApiError(503, 'Service Unavailable')
    expect(shouldRetryQuery(4, unavailable)).toBe(true)
    expect(shouldRetryQuery(5, unavailable)).toBe(false)
  })
})
