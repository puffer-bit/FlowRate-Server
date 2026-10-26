import { Badge, Tooltip } from '@mantine/core'
import type { SfuNode, SfuNodeConnectionState } from '../api/client'
import { formatDateTime } from '../format'

const colors: Record<SfuNodeConnectionState, string> = {
  Connected: 'green',
  Connecting: 'yellow',
  Unresponsive: 'orange',
  Disconnected: 'red',
}

/** Connection state of a node with its last ping; the tooltip tells why it is not connected. */
export function SfuNodeStatusBadge({ node }: { node: SfuNode }) {
  const status = node.status
  if (!node.isEnabled || !status)
    return <Badge color="gray" variant="light">disabled</Badge>

  const label = status.state === 'Connected' && status.pingMs != null
    ? `Connected · ${formatPing(status.pingMs)}`
    : status.state
  const details = [
    status.error,
    status.lastPingAtUtc && `Last answered ping: ${formatDateTime(status.lastPingAtUtc)}`,
  ].filter(Boolean).join('\n')

  return (
    <Tooltip label={details} disabled={!details} multiline maw={320} style={{ whiteSpace: 'pre-line' }}>
      <Badge color={colors[status.state]} variant="light">{label}</Badge>
    </Tooltip>
  )
}

const formatPing = (ms: number) => ms < 10 ? `${ms.toFixed(1)} ms` : `${Math.round(ms)} ms`
