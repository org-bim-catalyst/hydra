import { Alert, Box, Container, Snackbar, Tab, Tabs, Typography } from '@mui/material'
import { useState } from 'react'
import { AppShell } from '../../../components/AppShell'
import { useIsAdmin } from '../../../hooks/useIsAdmin'
import { DocumentCard } from '../components/DocumentCard'
import { DocumentDetailPanel } from '../components/DocumentDetailPanel'
import { DocumentFilterBar } from '../components/DocumentFilterBar'
import { DocumentFolderTree } from '../components/DocumentFolderTree'
import { NotificationInbox } from '../components/NotificationInbox'
import { OrganizationDashboard } from '../components/OrganizationDashboard'
import { ProcessingDashboard } from '../components/ProcessingDashboard'
import { UploadPanel } from '../components/UploadPanel'
import { useDashboard, useDocuments, useOrganizationDashboard } from '../hooks/useDocuments'
import { useNotificationHub } from '../hooks/useNotificationHub'
import type { DocumentListView, DocumentSearchFilters, DocumentSummary } from '../api/documentsApi'

/** The Document Intelligence Pipeline workspace (US1 upload/manage; US2 live processing status; US4 folders/search; US6 dashboard and notifications). */
export function DocumentWorkspacePage() {
  const [view, setView] = useState<DocumentListView>('Active')
  const [selectedFolderId, setSelectedFolderId] = useState<string | null>(null)
  const [filters, setFilters] = useState<DocumentSearchFilters>({})
  const [detailDocument, setDetailDocument] = useState<DocumentSummary | null>(null)
  const { data, isLoading, isError } = useDocuments(view, selectedFolderId, filters)

  const isAdmin = useIsAdmin()
  const dashboard = useDashboard()
  const organizationDashboard = useOrganizationDashboard(isAdmin)
  const { latest: latestNotification, dismiss: dismissNotification } = useNotificationHub()

  return (
    <AppShell
      title="Documents"
      subtitle="Upload, process, and manage your documents"
      actions={<NotificationInbox />}
    >
      <Container maxWidth="lg" disableGutters>
        <Box sx={{ mb: 3 }}>
          <ProcessingDashboard
            data={dashboard.data}
            isLoading={dashboard.isLoading}
            isError={dashboard.isError}
          />
        </Box>

        {isAdmin && (
          <Box sx={{ mb: 3 }}>
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              Organization-wide (administrator view)
            </Typography>
            <OrganizationDashboard
              data={organizationDashboard.data}
              isLoading={organizationDashboard.isLoading}
              isError={organizationDashboard.isError}
            />
          </Box>
        )}

        <Box sx={{ mb: 3 }}>
          <UploadPanel />
        </Box>

        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '220px 1fr' }, gap: 3 }}>
          <Box>
            <DocumentFolderTree
              selectedFolderId={selectedFolderId}
              onSelectFolder={setSelectedFolderId}
            />
          </Box>

          <Box>
            <Tabs
              value={view}
              onChange={(_, next: DocumentListView) => setView(next)}
              sx={{ mb: 2 }}
            >
              <Tab label="Active" value="Active" />
              <Tab label="Archived" value="Archived" />
              <Tab label="Deleted" value="Deleted" />
            </Tabs>

            <DocumentFilterBar filters={filters} onChange={setFilters} />

            {isError && (
              <Alert severity="error">Could not load your documents. Please try again.</Alert>
            )}

            {!isLoading && !isError && data?.items.length === 0 && (
              <Typography variant="body2" color="text.secondary">
                No documents match your current filters.
              </Typography>
            )}

            <Box
              sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(3, 1fr)' },
                gap: 2,
              }}
            >
              {data?.items.map((document) => (
                <DocumentCard
                  key={document.id}
                  document={document}
                  view={view}
                  onOpenDetail={setDetailDocument}
                />
              ))}
            </Box>
          </Box>
        </Box>

        <DocumentDetailPanel document={detailDocument} onClose={() => setDetailDocument(null)} />

        <Snackbar
          open={Boolean(latestNotification)}
          autoHideDuration={6000}
          onClose={dismissNotification}
        >
          <Alert severity="info" variant="filled" onClose={dismissNotification}>
            {latestNotification?.message}
          </Alert>
        </Snackbar>
      </Container>
    </AppShell>
  )
}
