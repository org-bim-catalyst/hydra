import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined'
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined'
import ExtensionOutlinedIcon from '@mui/icons-material/ExtensionOutlined'
import HandshakeOutlinedIcon from '@mui/icons-material/HandshakeOutlined'
import InsertDriveFileOutlinedIcon from '@mui/icons-material/InsertDriveFileOutlined'
import MenuBookOutlinedIcon from '@mui/icons-material/MenuBookOutlined'
import BuildOutlinedIcon from '@mui/icons-material/BuildOutlined'
import SmartToyOutlinedIcon from '@mui/icons-material/SmartToyOutlined'
import SyncAltOutlinedIcon from '@mui/icons-material/SyncAltOutlined'
import type { SvgIconComponent } from '@mui/icons-material'
import type { WorkflowNodeCategory } from '../nodeCatalog'

const CATEGORY_ICONS: Record<WorkflowNodeCategory, SvgIconComponent> = {
  AI: SmartToyOutlinedIcon,
  Knowledge: MenuBookOutlinedIcon,
  Documents: DescriptionOutlinedIcon,
  Files: InsertDriveFileOutlinedIcon,
  Tools: BuildOutlinedIcon,
  'Control Flow': AccountTreeOutlinedIcon,
  'Human Interaction': HandshakeOutlinedIcon,
  'Data Transformation': SyncAltOutlinedIcon,
  Integration: ExtensionOutlinedIcon,
}

export function getCategoryIcon(category: WorkflowNodeCategory): SvgIconComponent {
  return CATEGORY_ICONS[category]
}
