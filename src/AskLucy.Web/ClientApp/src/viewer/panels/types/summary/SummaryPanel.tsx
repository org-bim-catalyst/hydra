import { Typography } from '@mui/material'
import { z } from 'zod'
import { panelTypeRegistry } from '../../registry'

/** contracts/panel-type-registry.md "summary" built-in type — covers the spec's "Design
 * recommendations", "Site analysis", and "Alternative design proposals" categories via one
 * titled-heading + narrative-body primitive. Panel↔viewer communication (FR-013/FR-014) is
 * generic to any panel type via `FloatingPanel.tsx`'s context-association chrome (research.md/
 * data-model.md `ViewerContextAssociation` lives on the panel, not the type), not special-cased
 * here — this renderer only presents content. */
export const summaryDataSchema = z.object({
  heading: z.string(),
  body: z.string(),
})

export type SummaryData = z.infer<typeof summaryDataSchema>

function SummaryPanelRenderer({ data }: { data: SummaryData }) {
  return (
    <>
      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        {data.heading}
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'pre-wrap' }}>
        {data.body}
      </Typography>
    </>
  )
}

panelTypeRegistry.register({
  typeKey: 'summary',
  renderer: SummaryPanelRenderer,
  schema: summaryDataSchema,
  defaultSize: { width: 360, height: 280 },
  resizable: true,
})
