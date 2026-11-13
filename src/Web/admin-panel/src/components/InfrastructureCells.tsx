import { Anchor, Tooltip } from '@mantine/core'
import { Link } from 'react-router'
import { PanelIcon, type IconName } from './PanelIcon'

export function TableLabel({ icon, children }: { icon: IconName, children: string }) {
  return <span className="table-label"><PanelIcon name={icon} size={13} />{children}</span>
}

export function NodeIdentity({ name, icon, state, to }: { name: string, icon: IconName, state: string, to?: string }) {
  const tone = state === 'Online' || state === 'Connected' ? 'online'
    : state === 'Failed' || state === 'Disconnected' ? 'danger'
    : ['Starting', 'Restarting', 'Closing', 'Connecting', 'Unresponsive'].includes(state) ? 'warning' : 'muted'

  return (
    <div className="node-identity">
      <span className={`node-icon node-icon--${tone}`}><PanelIcon name={icon} size={17} /></span>
      {to
        ? <Anchor component={Link} to={to} className="node-name">{name}</Anchor>
        : <span className="node-name">{name}</span>}
    </div>
  )
}

export function NodeAddress({ host, port, secured }: { host: string, port: number, secured?: boolean }) {
  const address = `${host.includes(':') && !host.startsWith('[') ? `[${host}]` : host}:${port}`
  const label = secured === undefined ? 'Listen address' : secured ? 'TLS connection' : 'Unencrypted connection'
  return <Tooltip label={label}><span className="node-address"><PanelIcon name={secured ? 'shield' : 'network'} size={13} /><span>{address}</span></span></Tooltip>
}

export function NodeDuration({ value }: { value: string }) {
  return <span className="node-duration"><PanelIcon name="clock" size={13} />{value}</span>
}

export function TableMetric({ value }: { value: number | null | undefined }) {
  return <span className={`table-metric${value == null || value === 0 ? ' is-muted' : ''}`}>{value ?? '—'}</span>
}
