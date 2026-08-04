import { Box, Card, CardContent, Skeleton, Typography } from '@mui/material'
import type { KnowledgeBaseDashboardSummary } from '../api/knowledgeBasesApi'

function formatStorageSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`
}

interface KnowledgeBaseStatCardsProps {
  summary: KnowledgeBaseDashboardSummary | undefined
  isLoading: boolean
}

/** Dashboard summary statistics cards (FR-029) — reflects the user's current Active knowledge bases (archived ones are counted separately, not folded into the totals). */
export function KnowledgeBaseStatCards({ summary, isLoading }: KnowledgeBaseStatCardsProps) {
  const stats: { label: string; value: string }[] = [
    { label: 'Knowledge bases', value: String(summary?.totalKnowledgeBases ?? 0) },
    { label: 'Documents', value: String(summary?.totalDocuments ?? 0) },
    { label: 'Storage used', value: formatStorageSize(summary?.totalStorageBytes ?? 0) },
    { label: 'Updated recently', value: String(summary?.recentCount ?? 0) },
    { label: 'Favorites', value: String(summary?.favoritesCount ?? 0) },
    { label: 'Pinned', value: String(summary?.pinnedCount ?? 0) },
    { label: 'Archived', value: String(summary?.archivedCount ?? 0) },
  ]

  return (
    <Box
      data-testid="knowledge-base-stat-cards"
      sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: 'repeat(4, 1fr)', md: 'repeat(7, 1fr)' }, gap: 1.5, mb: 3 }}
    >
      {stats.map((stat) => (
        <Card key={stat.label} variant="outlined">
          <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
            <Typography variant="caption" color="text.secondary" noWrap>
              {stat.label}
            </Typography>
            {isLoading ? (
              <Skeleton variant="text" width="60%" height={32} />
            ) : (
              <Typography variant="h6">{stat.value}</Typography>
            )}
          </CardContent>
        </Card>
      ))}
    </Box>
  )
}
