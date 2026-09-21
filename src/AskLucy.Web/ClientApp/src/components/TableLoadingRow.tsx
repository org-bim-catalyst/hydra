import { Box, Skeleton, TableCell, TableRow } from '@mui/material'
import { visuallyHidden } from '@mui/utils'

interface TableLoadingRowProps {
  /** The table's column count — one skeleton cell is drawn per column. */
  colSpan: number
  /** How many placeholder rows to draw. The default fills a table without overflowing a short one. */
  rows?: number
}

/**
 * Shared table placeholder: skeleton rows in the shape of the real ones, matching the dashboard's
 * loading treatment. A spinner in the middle of an empty table says only "wait"; a skeleton also
 * says how many columns are coming and where they sit, so the layout does not jump when the data
 * lands.
 *
 * Renders a fragment of rows rather than a single row, so callers drop it straight into a
 * `<TableBody>`. Screen readers get the wait announced once, since a stack of decorative bars
 * conveys nothing without sight.
 */
export function TableLoadingRow({ colSpan, rows = 6 }: TableLoadingRowProps) {
  return (
    <>
      {Array.from({ length: rows }, (_, row) => (
        <TableRow key={row}>
          {Array.from({ length: colSpan }, (_, column) => (
            <TableCell key={column}>
              {row === 0 && column === 0 && (
                <Box component="span" role="status" sx={visuallyHidden}>
                  Loading… please wait
                </Box>
              )}
              <Skeleton variant="text" width={column === 0 ? '55%' : '35%'} />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  )
}
