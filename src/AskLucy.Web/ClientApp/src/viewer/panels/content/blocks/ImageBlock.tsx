import { Box, Skeleton, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '../../../../api/httpClient'
import { resolveSignedUrl } from '../../../../features/documents/api/documentsApi'
import type { ImageBlock as ImageBlockData } from '../blocks'

/** contracts/content-vocabulary.md "image" block. `fileId` resolves only through the platform's
 * existing file-access mechanism and its entitlement checks (research D10) — the schema already
 * rejects an external address outright, so this component never receives one to render; it only
 * has to handle "cannot be reached" and "not entitled", which the download endpoint's own
 * authorization surfaces as an ordinary failed request. */
export function ImageBlockRenderer({ block }: { block: ImageBlockData }) {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['panel-content-image', block.fileId],
    queryFn: () => apiFetch<{ url: string }>(`/documents/${block.fileId}/download`),
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
        alt={block.alt}
        style={{ maxWidth: '100%', height: 'auto', display: 'block', borderRadius: 4 }}
      />
    </Box>
  )
}
