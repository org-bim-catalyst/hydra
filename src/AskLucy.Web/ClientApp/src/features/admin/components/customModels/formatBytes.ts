/** Mirrors the server's CustomModel.FormatBytes so the size-limit message and the table agree. */
export function formatBytes(bytes: number): string {
  const units: [number, string][] = [
    [2 ** 30, 'GB'],
    [2 ** 20, 'MB'],
    [2 ** 10, 'KB'],
  ]
  for (const [size, unit] of units) {
    if (bytes >= size) return `${Number((bytes / size).toFixed(2))} ${unit}`
  }
  return `${bytes} bytes`
}
