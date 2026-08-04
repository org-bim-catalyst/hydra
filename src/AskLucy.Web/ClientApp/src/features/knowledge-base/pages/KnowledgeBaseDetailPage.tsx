import { Box, Paper, Typography } from '@mui/material'
import { useParams } from 'react-router'
import { PageHeader } from '../../../components/PageHeader'
import { useKnowledgeBase } from '../hooks/useKnowledgeBases'
import { DocumentUploadZone } from '../components/DocumentUploadZone'
import { KnowledgeBaseFolderTree } from '../components/KnowledgeBaseFolderTree'

/**
 * A single knowledge base's folder tree and document organization view (FR-012–FR-016,
 * User Story 2). Reached by opening a card from `KnowledgeBaseDashboardPage`.
 */
export function KnowledgeBaseDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { data: knowledgeBase, isLoading } = useKnowledgeBase(id ?? null)

  if (!id) {
    return null
  }

  return (
    <Box sx={{ p: { xs: 2, sm: 4 }, bgcolor: 'background.default', minHeight: '100%' }}>
      <PageHeader
        backTo="/knowledge-bases"
        backLabel="Back to Knowledge Bases"
        title={knowledgeBase?.name ?? (isLoading ? 'Loading…' : 'Knowledge Base')}
        subtitle={knowledgeBase?.description ?? undefined}
      />

      <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          Upload a document to the root
        </Typography>
        <DocumentUploadZone knowledgeBaseId={id} targetFolderId={null} />
      </Paper>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          Folders &amp; documents
        </Typography>
        <KnowledgeBaseFolderTree knowledgeBaseId={id} />
      </Paper>
    </Box>
  )
}
