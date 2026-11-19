import type { ReactNode } from 'react'
import { PanelIcon, type IconName } from './PanelIcon'

export function StatCard({ label, value, icon = 'activity', hint }: { label: string, value: ReactNode, icon?: IconName, hint?: string }) {
  return (
    <div className="stat-card">
      <div className="stat-card-top"><span>{label}</span><span className="stat-icon"><PanelIcon name={icon} size={18} /></span></div>
      <div className="stat-value">{value}</div>
      {hint && <div className="stat-hint">{hint}</div>}
    </div>
  )
}
