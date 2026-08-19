import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { z } from 'zod'
import { panelTypeRegistry } from '../../registry'

/** contracts/panel-type-registry.md "table" built-in type — covers the spec's "Tables",
 * "GIS information", and "analysis dashboards" categories. Cell values are rendered as plain text
 * (React's default escaping), never `dangerouslySetInnerHTML` (constitution §8 XSS rule) — AI/tool
 * output is untrusted data, not markup. */
export const tableDataSchema = z.object({
  columns: z.array(z.string()).min(1),
  rows: z.array(z.array(z.union([z.string(), z.number(), z.null()]))),
})

export type TableData = z.infer<typeof tableDataSchema>

function TablePanelRenderer({ data }: { data: TableData }) {
  if (data.rows.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        No data to display.
      </Typography>
    )
  }

  return (
    <TableContainer>
      <Table size="small" aria-label="Panel data table">
        <TableHead>
          <TableRow>
            {data.columns.map((column) => (
              <TableCell key={column}>{column}</TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {data.rows.map((row, rowIndex) => (
            // Rows carry no stable identity in this generic payload — index is the only option.
            <TableRow key={rowIndex}>
              {row.map((cell, cellIndex) => (
                <TableCell key={cellIndex}>{cell ?? '—'}</TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  )
}

panelTypeRegistry.register({
  typeKey: 'table',
  renderer: TablePanelRenderer,
  schema: tableDataSchema,
  defaultSize: { width: 480, height: 360 },
  resizable: true,
})
