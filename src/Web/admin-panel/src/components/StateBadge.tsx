import { Badge } from '@mantine/core'
import type { CoordinatorState } from '../api/client'

const colors: Record<CoordinatorState, string> = {
  Online: 'green',
  Starting: 'yellow',
  Restarting: 'yellow',
  Closing: 'yellow',
  Offline: 'gray',
  Closed: 'gray',
  Failed: 'red',
}

export function StateBadge({ state }: { state: CoordinatorState }) {
  return <Badge color={colors[state]} variant="light">{state}</Badge>
}
