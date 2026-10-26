import { Alert, Anchor, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core'
import { Link } from 'react-router'
import type { SfuNode } from '../api/client'
import { useNow, useOverview, useSfuNodes } from '../api/hooks'
import { formatSince } from '../format'
import { QueryState } from '../components/QueryState'
import { SfuNodeStatusBadge } from '../components/SfuNodeStatusBadge'
import { StatCard } from '../components/StatCard'
import { StateBadge } from '../components/StateBadge'

export function OverviewPage() {
  const overview = useOverview()
  const now = useNow()

  return (
    <QueryState query={overview}>
      {data => (
        <Stack>
          <SimpleGrid cols={{ base: 2, sm: 4 }}>
            <StatCard label="Coordinators" value={data.coordinators.length} />
            <StatCard label="Active sessions" value={data.activeSessionsCount} />
            <StatCard label="Connected users" value={data.connectedUsersCount} />
            <StatCard label="Uptime" value={formatSince(data.startedAtUtc, now)} />
          </SimpleGrid>

          <Title order={3}>Coordinators</Title>
          {data.coordinators.length === 0
            ? <Text c="dimmed">No coordinators are running.</Text>
            : (
              <Table.ScrollContainer minWidth={640}>
                <Table striped highlightOnHover>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Name</Table.Th>
                      <Table.Th>Address</Table.Th>
                      <Table.Th>State</Table.Th>
                      <Table.Th>Uptime</Table.Th>
                      <Table.Th ta="right">Sessions</Table.Th>
                      <Table.Th ta="right">Active</Table.Th>
                      <Table.Th ta="right">Users</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {data.coordinators.map(coordinator => (
                      <Table.Tr key={coordinator.id}>
                        <Table.Td>
                          <Anchor component={Link} to={`/coordinators/${coordinator.id}`}>{coordinator.name}</Anchor>
                        </Table.Td>
                        <Table.Td>{coordinator.ip}:{coordinator.port}</Table.Td>
                        <Table.Td><StateBadge state={coordinator.state} /></Table.Td>
                        <Table.Td>{formatSince(coordinator.startedAtUtc, now)}</Table.Td>
                        <Table.Td ta="right">{coordinator.sessionsCount}</Table.Td>
                        <Table.Td ta="right">{coordinator.activeSessionsCount}</Table.Td>
                        <Table.Td ta="right">{coordinator.connectedUsersCount}</Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            )}

          <SfuNodesSection />

          <Text size="sm" c="dimmed">Server version {data.serverVersion}</Text>
        </Stack>
      )}
    </QueryState>
  )
}

function SfuNodesSection() {
  const sfuNodes = useSfuNodes()
  const now = useNow()

  return (
    <>
      <Title order={3}>SFU nodes</Title>
      <QueryState query={sfuNodes}>
        {nodes => nodes.length === 0
          ? <Text c="dimmed">The orchestrator has no SFU nodes. Voice sessions do not work until one is added.</Text>
          : (
            <Stack gap="sm">
              <SfuNodesAlert nodes={nodes} />
              <Table.ScrollContainer minWidth={640}>
                <Table striped highlightOnHover>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Name</Table.Th>
                      <Table.Th>Address</Table.Th>
                      <Table.Th>State</Table.Th>
                      <Table.Th>For</Table.Th>
                      <Table.Th ta="right">Sessions</Table.Th>
                      <Table.Th ta="right">Workers</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {nodes.map(node => (
                      <Table.Tr key={node.id}>
                        <Table.Td>{node.name}</Table.Td>
                        <Table.Td>{node.host}:{node.port}</Table.Td>
                        <Table.Td><SfuNodeStatusBadge node={node} /></Table.Td>
                        <Table.Td>{node.status ? formatSince(node.status.stateChangedAtUtc, now) : '—'}</Table.Td>
                        <Table.Td ta="right">{node.status?.sessionCount ?? '—'}</Table.Td>
                        <Table.Td ta="right">{node.status?.workerCount ?? '—'}</Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            </Stack>
          )}
      </QueryState>
    </>
  )
}

/** Enabled nodes that cannot take sessions right now. */
const isSfuNodeDown = (node: SfuNode) => node.isEnabled && node.status != null && node.status.state !== 'Connected'

function SfuNodesAlert({ nodes }: { nodes: SfuNode[] }) {
  const down = nodes.filter(isSfuNodeDown)
  if (down.length === 0)
    return null

  const allDown = nodes.filter(node => node.isEnabled).length === down.length
  return (
    <Alert color={allDown ? 'red' : 'orange'}
      title={allDown ? 'No SFU node is connected' : `${down.length} SFU node${down.length > 1 ? 's are' : ' is'} not connected`}>
      {allDown
        ? 'Voice sessions cannot start until a node is back. Reconnecting automatically.'
        : `${down.map(node => node.name).join(', ')}: sessions there wait until the node is back. Reconnecting automatically.`}
    </Alert>
  )
}
