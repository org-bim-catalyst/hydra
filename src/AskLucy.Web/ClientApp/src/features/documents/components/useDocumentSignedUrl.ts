import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '../../../api/httpClient'

/**
 * The fresh signed URL for a stored image, or the raw download for any document.
 *
 * <p>
 * One query, one request: `DocumentImage` and the chat bubble's download button both call this,
 * and React Query deduplicates on the key — so a picture in a chat bubble is fetched once, not
 * once per consumer.
 * </p>
 *
 * <p>
 * The address is resolved at call time and expires, so it is never persisted; entitlement is
 * enforced by the download endpoint's own authorization.
 * </p>
 */
export function useDocumentSignedUrl(documentId: string) {
  return useQuery({
    queryKey: ['document-image', documentId],
    queryFn: () => apiFetch<{ url: string; fileName: string }>(`/documents/${documentId}/download`),
  })
}
