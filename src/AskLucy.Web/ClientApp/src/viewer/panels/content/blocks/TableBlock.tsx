import { RiErrorWarningLine } from '@remixicon/react'
import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Tooltip, Typography } from '@mui/material'
import { ActionAffordance } from '../../actions/ActionAffordance'
import type { TableBlock as TableBlockData } from '../blocks'

/** contracts/content-vocabulary.md "table" block — ported from the retired `table` panel type.
 * A row whose `cells` length differs from `columns.length` renders what is present (padded or
 * truncated to fit) and is visibly marked malformed, rather than failing the whole block
 * (data-model.md, spec User Story 4).
 *
 * A row's action, if valid, is placed on its first cell rather than the whole `<tr>` — see
 * `ActionAffordance`'s own doc comment for why: making a whole row a button overrides its
 * `role="row"`, breaking the row/cell navigation assistive technology depends on. This keeps one
 * unambiguous, keyboard-reachable control per actionable row (spec User Story 2). */
export function TableBlockRenderer({ block }: { block: TableBlockData }) {
  if (block.rows.length === 0) {
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
            {block.columns.map((column) => (
              <TableCell key={column}>{column}</TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {block.rows.map((row, rowIndex) => {
            const malformed = row.cells.length !== block.columns.length
            const cells = block.columns.map((_, columnIndex) => row.cells[columnIndex] ?? null)
            return (
              <TableRow key={rowIndex} sx={malformed ? { bgcolor: 'warning.main', opacity: 0.08 } : undefined}>
                {cells.map((cell, cellIndex) => {
                  const content = (
                    <>
                      {cellIndex === 0 && malformed && (
                        <Tooltip title="This row's data didn't match the table's columns">
                          <span role="img" aria-label="Row is malformed" style={{ marginRight: 4, display: 'inline-flex', verticalAlign: 'middle' }}>
                            <RiErrorWarningLine size={14} />
                          </span>
                        </Tooltip>
                      )}
                      {cell ?? '—'}
                    </>
                  )
                  return (
                    <TableCell key={cellIndex}>
                      {cellIndex === 0 ? <ActionAffordance action={row.action}>{content}</ActionAffordance> : content}
                    </TableCell>
                  )
                })}
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </TableContainer>
  )
}
