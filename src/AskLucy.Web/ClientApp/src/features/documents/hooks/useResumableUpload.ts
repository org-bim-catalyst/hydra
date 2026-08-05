import { useCallback, useRef, useState } from 'react'
import * as documentsApi from '../api/documentsApi'
import type { DocumentSummary } from '../api/documentsApi'

/** Files at or above this size use the chunked/resumable flow (FR-005); smaller files use the single-request path (contracts/documents-api.md). */
const CHUNKED_UPLOAD_THRESHOLD_BYTES = 20 * 1024 * 1024

export type ResumableUploadStatus = 'idle' | 'uploading' | 'duplicate' | 'completed' | 'error' | 'cancelled'

export interface DuplicateInfo {
  uploadSessionId: string
  duplicateOfDocumentId: string
}

export function useResumableUpload(file: File) {
  const [status, setStatus] = useState<ResumableUploadStatus>('idle')
  const [progress, setProgress] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [duplicateInfo, setDuplicateInfo] = useState<DuplicateInfo | null>(null)
  const [document, setDocument] = useState<DocumentSummary | null>(null)
  const cancelledRef = useRef(false)

  const runChunkedUpload = useCallback(async () => {
    const session = await documentsApi.startUpload(file.name, file.size)
    const chunkSize = session.chunkSizeBytes

    let offset = 0
    let chunkIndex = 0
    while (offset < file.size) {
      if (cancelledRef.current) {
        await documentsApi.cancelUpload(session.uploadSessionId)
        setStatus('cancelled')
        return
      }

      const chunk = file.slice(offset, offset + chunkSize)
      const result = await documentsApi.uploadChunk(session.uploadSessionId, chunkIndex, chunk)
      offset += chunk.size
      chunkIndex = result.nextExpectedChunkIndex
      setProgress(Math.min(99, Math.round((offset / file.size) * 100)))
    }

    const completion = await documentsApi.completeUpload(session.uploadSessionId)
    if (completion.isDuplicate) {
      setDuplicateInfo({ uploadSessionId: session.uploadSessionId, duplicateOfDocumentId: completion.duplicateOfDocumentId! })
      setStatus('duplicate')
      return
    }

    setDocument(completion.document)
    setProgress(100)
    setStatus('completed')
  }, [file])

  const runSimpleUpload = useCallback(async () => {
    const result = await documentsApi.simpleUpload(file)
    if (result.isDuplicate) {
      setDuplicateInfo({ uploadSessionId: result.uploadSessionId!, duplicateOfDocumentId: result.duplicateOfDocumentId! })
      setStatus('duplicate')
      return
    }

    setDocument(result.document)
    setProgress(100)
    setStatus('completed')
  }, [file])

  const start = useCallback(() => {
    cancelledRef.current = false
    setStatus('uploading')
    setError(null)
    setProgress(0)

    const upload = file.size >= CHUNKED_UPLOAD_THRESHOLD_BYTES ? runChunkedUpload() : runSimpleUpload()
    upload.catch((err: unknown) => {
      setStatus('error')
      setError(err instanceof Error ? err.message : 'Upload failed. Please try again.')
    })
  }, [file, runChunkedUpload, runSimpleUpload])

  const cancel = useCallback(() => {
    cancelledRef.current = true
  }, [])

  const resolveDuplicateAsVersion = useCallback(
    async (versionIncrement: 'Major' | 'Minor') => {
      if (!duplicateInfo) return
      try {
        const result = await documentsApi.completeUploadAsVersion(
          duplicateInfo.uploadSessionId,
          duplicateInfo.duplicateOfDocumentId,
          versionIncrement,
        )
        setDocument(result)
        setStatus('completed')
      } catch (err) {
        setStatus('error')
        setError(err instanceof Error ? err.message : 'Failed to resolve duplicate.')
      }
    },
    [duplicateInfo],
  )

  const resolveDuplicateAsNew = useCallback(async () => {
    if (!duplicateInfo) return
    try {
      const result = await documentsApi.completeUploadAsNew(duplicateInfo.uploadSessionId)
      setDocument(result)
      setStatus('completed')
    } catch (err) {
      setStatus('error')
      setError(err instanceof Error ? err.message : 'Failed to resolve duplicate.')
    }
  }, [duplicateInfo])

  return { status, progress, error, duplicateInfo, document, start, cancel, resolveDuplicateAsVersion, resolveDuplicateAsNew }
}
