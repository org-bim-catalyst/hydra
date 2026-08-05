import AttachFileIcon from '@mui/icons-material/AttachFile'
import { Alert, Box, Chip, Paper, Stack, Typography } from '@mui/material'
import 'katex/dist/katex.min.css'
import ReactMarkdown from 'react-markdown'
import rehypeKatex from 'rehype-katex'
import remarkGfm from 'remark-gfm'
import remarkMath from 'remark-math'
import type { ChatMessage } from '../api/aiApi'
import { CitationBadge } from '../../retrieval/components/CitationBadge'

/** Renders Markdown + KaTeX math (FR-007), preserved from the legacy chat UI. */
export function MessageBubble({ message }: { message: ChatMessage }) {
  const isUser = message.role === 'user'
  const hasAttachments = (message.attachments?.length ?? 0) > 0
  const hasCitations = (message.citations?.length ?? 0) > 0
  const hasAttribution = !isUser && Boolean(message.provider || message.model)

  return (
    <Box sx={{ display: 'flex', justifyContent: isUser ? 'flex-end' : 'flex-start', mb: 2 }}>
      <Paper
        elevation={isUser ? 0 : 1}
        sx={{
          px: 2,
          py: 1.25,
          maxWidth: '75%',
          borderRadius: 3,
          ...(isUser && { borderBottomRightRadius: 4 }),
          ...(!isUser && { borderBottomLeftRadius: 4 }),
          bgcolor: isUser ? 'primary.main' : 'background.paper',
          color: isUser ? 'primary.contrastText' : 'text.primary',
          '& p:first-of-type': { mt: 0 },
          '& p:last-of-type': { mb: 0 },
          '& img': { maxWidth: '100%', borderRadius: 1 },
        }}
      >
        <Typography component="div" variant="body1">
          <ReactMarkdown remarkPlugins={[remarkGfm, remarkMath]} rehypePlugins={[rehypeKatex]}>
            {message.content}
          </ReactMarkdown>
        </Typography>

        {(hasAttachments || hasCitations) && (
          <Stack direction="row" spacing={0.5} sx={{ mt: 1, flexWrap: 'wrap' }}>
            {message.attachments?.map((a) => (
              <Chip
                key={a.id}
                size="small"
                icon={<AttachFileIcon />}
                label={a.fileName}
                component="a"
                href={a.accessLocation}
                target="_blank"
                rel="noopener noreferrer"
                clickable
              />
            ))}
            {message.citations?.map((c) => (
              <CitationBadge key={c.id} citation={c} />
            ))}
          </Stack>
        )}

        {/* specs/016-rag-semantic-search US1 (research.md Decision 8) — a non-silent, visible
            indicator for a degraded response: NoRelevantContent means the message answered
            without grounding because nothing relevant was found; Unavailable means retrieval
            itself failed but the response still generated (FR-037a — never blocked, never silent). */}
        {message.retrievalOutcome === 'NoRelevantContent' && (
          <Alert severity="info" variant="outlined" sx={{ mt: 1, py: 0 }}>
            No relevant content was found in the attached knowledge base(s) — this response isn't grounded in your documents.
          </Alert>
        )}
        {message.retrievalOutcome === 'Unavailable' && (
          <Alert severity="warning" variant="outlined" sx={{ mt: 1, py: 0 }}>
            {message.retrievalError ?? 'Knowledge base retrieval was temporarily unavailable — this response isn\'t grounded in your documents.'}
          </Alert>
        )}

        {/* specs/005-multi-provider-ai-engine FR-011: attribution is a snapshot of what
            actually produced this message, independent of the conversation's current
            provider/model selection. */}
        {(hasAttribution || message.isIncomplete) && (
          <Stack direction="row" spacing={0.5} sx={{ mt: 1, flexWrap: 'wrap' }}>
            {hasAttribution && (
              <Chip size="small" variant="outlined" label={[message.provider, message.model].filter(Boolean).join(' · ')} />
            )}
            {message.isIncomplete && (
              <Chip size="small" color="warning" variant="outlined" label="Incomplete — connection dropped" />
            )}
          </Stack>
        )}
      </Paper>
    </Box>
  )
}
