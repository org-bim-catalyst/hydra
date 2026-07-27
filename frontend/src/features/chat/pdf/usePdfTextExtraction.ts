import { useCallback } from 'react'

/**
 * Client-side PDF text extraction (FR-005) — unchanged from the legacy app: no file is
 * ever uploaded to the server for this step, `pdfjs-dist` runs entirely in the browser.
 */
export function usePdfTextExtraction() {
  const extractText = useCallback(async (file: File): Promise<string> => {
    const pdfjs = await import('pdfjs-dist')
    pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/build/pdf.worker.mjs', import.meta.url).toString()

    const arrayBuffer = await file.arrayBuffer()
    const pdf = await pdfjs.getDocument({ data: arrayBuffer }).promise

    const pageTexts: string[] = []
    for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber++) {
      const page = await pdf.getPage(pageNumber)
      const content = await page.getTextContent()
      const pageText = content.items.map((item) => ('str' in item ? item.str : '')).join(' ')
      pageTexts.push(pageText)
    }

    return pageTexts.join('\n\n')
  }, [])

  return { extractText }
}
