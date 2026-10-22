import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material'
import { useMutation } from '@tanstack/react-query'
import { useT } from '../../../i18n/useT'
import { ApiError } from '../../../api/httpClient'
import { useCan } from '../../auth/hooks/usePermissions'
import * as adminAiToolsApi from '../api/adminAiToolsApi'
import type { ToolSetting, ToolSettings } from '../api/adminAiToolsApi'

interface ToolSettingsDialogProps {
  /** The tool being configured; the dialog is open while this is set. */
  toolSettings: ToolSettings | null
  onClose: () => void
  onSaved: () => void
  /** Called when the save failed, so the page can say so too. */
  onFailed?: (message: string) => void
}

/**
 * specs/083 — the settings one tool declares, opened from its gear on the AI Tools page. Changes
 * are held here until Save, so Cancel leaves the stored values untouched.
 *
 * The labels and descriptions are the server's own words, so they are shown verbatim: the server
 * owns what each setting means, and a translation would only risk saying something the server
 * does not do.
 */
export function ToolSettingsDialog({
  toolSettings,
  onClose,
  onSaved,
  onFailed,
}: ToolSettingsDialogProps) {
  return (
    <Dialog open={toolSettings !== null} onClose={onClose} maxWidth="sm" fullWidth>
      {/*
        Keyed on the tool so every opening starts from the stored values, not from edits left
        behind by a cancelled one.
      */}
      {toolSettings && (
        <ToolSettingsForm
          key={toolSettings.tool}
          toolSettings={toolSettings}
          onClose={onClose}
          onSaved={onSaved}
          onFailed={onFailed}
        />
      )}
    </Dialog>
  )
}

function ToolSettingsForm({
  toolSettings,
  onClose,
  onSaved,
  onFailed,
}: ToolSettingsDialogProps & { toolSettings: ToolSettings }) {
  const t = useT('admin.aiTools')
  // Reading the settings needs the view permission; changing them needs the manage one. A caller
  // with only the former sees the values and is told plainly that they cannot change them.
  const canManage = useCan('admin.ai-tools.manage')
  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(toolSettings.settings.map((setting) => [setting.key, setting.value])),
  )

  const saveMutation = useMutation({
    mutationFn: () => adminAiToolsApi.updateToolSettings(toolSettings.tool, values),
    onSuccess: onSaved,
    // The server's own explanation is shown inline in the dialog, right above the buttons that
    // retry it; the page says only that the save did not happen.
    onError: () => onFailed?.(t('feedback.failed')),
  })

  const unchanged = toolSettings.settings.every(
    (setting) => values[setting.key] === setting.value,
  )

  const setValue = (key: string, value: string) =>
    setValues((current) => ({ ...current, [key]: value }))

  return (
    <>
      <DialogTitle>{t('dialog.title', { tool: toolSettings.tool })}</DialogTitle>
      <DialogContent>
        {!canManage && <Alert severity="info" sx={{ mb: 2 }}>{t('dialog.viewOnly')}</Alert>}
        <Stack spacing={2} sx={{ mt: 1 }}>
          {toolSettings.settings.map((setting) => (
            <div key={setting.key}>
              <SettingControl
                setting={setting}
                value={values[setting.key] ?? ''}
                disabled={!canManage || saveMutation.isPending}
                onChange={(value) => setValue(setting.key, value)}
              />
              <Typography variant="body2" color="text.secondary" sx={descriptionIndent(setting)}>
                {setting.description}
              </Typography>
            </div>
          ))}
        </Stack>
        {saveMutation.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {saveMutation.error instanceof ApiError
              ? (saveMutation.error.detail ?? saveMutation.error.message)
              : t('dialog.failed')}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('dialog.cancel')}</Button>
        <Button
          variant="contained"
          disabled={unchanged || saveMutation.isPending || !canManage}
          onClick={() => saveMutation.mutate()}
        >
          {saveMutation.isPending ? t('dialog.saving') : t('dialog.save')}
        </Button>
      </DialogActions>
    </>
  )
}

/** The control for one setting, chosen by its value type. */
function SettingControl({
  setting,
  value,
  disabled,
  onChange,
}: {
  setting: ToolSetting
  value: string
  disabled: boolean
  onChange: (value: string) => void
}) {
  switch (setting.valueType) {
    case 'Boolean':
      return (
        <FormControlLabel
          control={
            <Switch
              checked={value === 'true'}
              disabled={disabled}
              onChange={(event) => onChange(event.target.checked ? 'true' : 'false')}
            />
          }
          label={setting.label}
        />
      )
    case 'Choice':
      return (
        <FormControl size="small" fullWidth>
          <InputLabel id={`${setting.key}-label`}>{setting.label}</InputLabel>
          <Select
            labelId={`${setting.key}-label`}
            size="small"
            value={value}
            disabled={disabled}
            onChange={(event) => onChange(String(event.target.value))}
            // The stored value is the wire value; the administrator reads the option's label.
            renderValue={(selected) =>
              setting.options.find((option) => option.value === selected)?.label ?? String(selected)
            }
          >
            {setting.options.map((option) => (
              <MenuItem key={option.value} value={option.value}>
                {option.label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      )
    case 'Integer':
      return (
        <TextField
          size="small"
          fullWidth
          type="number"
          label={setting.label}
          value={value}
          disabled={disabled}
          // The raw text is kept as typed: the server validates the range and says what is wrong.
          onChange={(event) => onChange(event.target.value)}
          slotProps={{
            htmlInput: {
              min: setting.minimum ?? undefined,
              max: setting.maximum ?? undefined,
            },
          }}
        />
      )
  }
}

/** A switch's description sits under its label; the others sit under the whole control. */
function descriptionIndent(setting: ToolSetting) {
  return setting.valueType === 'Boolean'
    ? { marginInlineStart: '48px' }
    : { marginTop: '4px' }
}
