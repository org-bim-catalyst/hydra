import DownloadIcon from '@mui/icons-material/Download'
import InsertDriveFileOutlinedIcon from '@mui/icons-material/InsertDriveFileOutlined'
import { Box, Button, List, ListItem, ListItemText, Typography } from '@mui/material'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import * as documentsApi from '../api/documentsApi'
import { resolveSignedUrl } from '../api/documentsApi'
import type { DocumentFileType } from '../api/documentsApi'
import { useDocumentPreview } from '../hooks/useDocuments'
import { EmptyState } from '../../../components/EmptyState'
import { ErrorState } from '../../../components/ErrorState'
import { SkeletonBlock } from '../../../components/SkeletonBlock'
import { radius } from '../../../theme'

interface StructureElement {
  type: string
  text: string
  level?: number
}

/** Read-only rendering of the extracted structure JSON for Office documents (FR-043) — headings/paragraphs/tables/lists, no pixel-perfect layout (research.md Decision 6). */
function StructuredContentView({ json }: { json: string }) {
  let elements: StructureElement[]
  try {
    elements = JSON.parse(json) as StructureElement[]
  } catch {
    return (
      <Typography variant="body2" color="text.secondary">
        Preview content could not be parsed.
      </Typography>
    )
  }

  return (
    <List dense>
      {elements.map((element, index) => {
        if (element.type === 'heading') {
          return (
            <ListItem key={index} disableGutters>
              <Typography variant={element.level === 1 ? 'h6' : 'subtitle1'}>{element.text}</Typography>
            </ListItem>
          )
        }

        if (element.type === 'list-item') {
          return (
            <ListItem key={index} disableGutters sx={{ pl: 2 }}>
              <ListItemText primary={`• ${element.text}`} />
            </ListItem>
          )
        }

        if (element.type === 'table' || element.type === 'table-row') {
          return (
            <ListItem key={index} disableGutters>
              <ListItemText secondary={element.text} slotProps={{ secondary: { sx: { fontFamily: 'monospace' } } }} />
            </ListItem>
          )
        }

        return (
          <ListItem key={index} disableGutters>
            <ListItemText primary={element.text} />
          </ListItem>
        )
      })}
    </List>
  )
}

interface DocumentPreviewPaneProps {
  documentId: string
  fileType: DocumentFileType
}

/** FR-043, FR-044 — an inline preview for PDF (page image), images (thumbnail), Office documents (structured content), and Markdown (rendered directly); offers download instead when unavailable, never an error. */
export function DocumentPreviewPane({ documentId, fileType }: DocumentPreviewPaneProps) {
  const { data, isLoading, isError, refetch } = useDocumentPreview(documentId)

  if (isError) {
    return <ErrorState title="Could not load the preview" onRetry={() => void refetch()} />
  }

  if (isLoading) {
    return <SkeletonBlock variant="card" />
  }

  if (!data || data.previewType === 'Unavailable') {
    return (
      <EmptyState
        icon={<InsertDriveFileOutlinedIcon fontSize="inherit" />}
        title="No preview available"
        action={
          <Button size="small" startIcon={<DownloadIcon fontSize="small" />} onClick={() => void documentsApi.downloadDocument(documentId)}>
            Download instead
          </Button>
        }
      />
    )
  }

  if (data.previewType === 'StructuredContent' && data.structuredContent) {
    return fileType === 'Markdown' ? (
      <Box sx={{ '& img': { maxWidth: '100%' } }}>
        <ReactMarkdown remarkPlugins={[remarkGfm]}>{data.structuredContent}</ReactMarkdown>
      </Box>
    ) : (
      <StructuredContentView json={data.structuredContent} />
    )
  }

  if (data.url) {
    return (
      <Box
        component="img"
        src={resolveSignedUrl(data.url)}
        alt={`Preview of ${documentId}`}
        sx={{ maxWidth: '100%', display: 'block', borderRadius: `${radius.sm}px` }}
      />
    )
  }

  return null
}
