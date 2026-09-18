import { Box, Skeleton, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '../../../api/httpClient'
import { resolveSignedUrl } from '../api/documentsApi'

interface DocumentImageProps {
  documentId: string
  alt: string
}

/**
 * Renders an image stored as a platform document — a generated chat image, a site-analysis map,
 * a panel image block. The address is always a fresh signed URL from
 * `GET /documents/{id}/download`, resolved at render time, so a stored reference never goes stale
 * the way a saved provider URL does, and entitlement is enforced by that endpoint's own
 * authorization ("not entitled" and "cannot be reached" both surface as an ordinary failed request).
 */
export function DocumentImage({ documentId, alt }: DocumentImageProps) {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['document-image', documentId],
    queryFn: () => apiFetch<{ url: string }>(`/documents/${documentId}/download`),
  })

  if (isLoading) {
    return <Skeleton variant="rectangular" height={160} sx={{ borderRadius: 1 }} />
  }

  if (isError || !data) {
    return (
      <Typography variant="body2" color="text.secondary">
        This image is unavailable.
      </Typography>
    )
  }

  return (
    <Box sx={{ maxWidth: '100%' }}>
      <img
        src={resolveSignedUrl(data.url)}
        alt={alt}
        style={{ maxWidth: '100%', height: 'auto', display: 'block', borderRadius: 4 }}
      />
    </Box>
  )
}
