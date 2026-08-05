import { useCallback, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import * as documentsApi from '../api/documentsApi'
import { DOCUMENTS_QUERY_KEY } from './useDocuments'

export type ReplaceDocumentStatus = 'idle' | 'uploading' | 'completed' | 'error'

/**
 * Chunked upload targeting an existing document (US5 FR-038/FR-039, contracts/document-versions-
 * folders-api.md's "same upload session flow ... targeted at an existing document"). No
 * duplicate-detection step here — unlike a plain new-document upload, replacing is always
 * treated as deliberately new content (see `ReplaceDocumentCommandHandler`'s doc comment).
 */
export function useReplaceDocument(documentId: string) {
  const [status, setStatus] = useState<ReplaceDocumentStatus>('idle')
  const [progress, setProgress] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()

  const start = useCallback(
    (file: File, versionIncrement: 'Major' | 'Minor') => {
      setStatus('uploading')
      setError(null)
      setProgress(0)

      const run = async () => {
        const session = await documentsApi.startUpload(file.name, file.size, documentId)
        const chunkSize = session.chunkSizeBytes

        let offset = 0
        let chunkIndex = 0
        while (offset < file.size) {
          const chunk = file.slice(offset, offset + chunkSize)
          const result = await documentsApi.uploadChunk(session.uploadSessionId, chunkIndex, chunk)
          offset += chunk.size
          chunkIndex = result.nextExpectedChunkIndex
          setProgress(Math.min(99, Math.round((offset / file.size) * 100)))
        }

        await documentsApi.replaceDocument(documentId, session.uploadSessionId, versionIncrement)
        setProgress(100)
        setStatus('completed')
        await queryClient.invalidateQueries({ queryKey: DOCUMENTS_QUERY_KEY })
      }

      run().catch((err: unknown) => {
        setStatus('error')
        setError(err instanceof Error ? err.message : 'Replace failed. Please try again.')
      })
    },
    [documentId, queryClient],
  )

  const reset = useCallback(() => {
    setStatus('idle')
    setProgress(0)
    setError(null)
  }, [])

  return { status, progress, error, start, reset }
}
