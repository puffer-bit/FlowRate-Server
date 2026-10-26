import { Alert, Anchor, Badge, Button, Group, SimpleGrid, Stack, Table, Text, Title, Tooltip } from '@mantine/core'
import { Link, useParams } from 'react-router'
import { isNotFound, type ConnectedUser, type HybridSession, type Peer } from '../api/client'
import { useCoordinator, useNow } from '../api/hooks'
import { formatDateTime, formatSince } from '../format'
import { QueryState } from '../components/QueryState'
import { StatCard } from '../components/StatCard'
import { StateBadge } from '../components/StateBadge'

export function CoordinatorPage() {
  const { id = '' } = useParams()
  const coordinator = useCoordinator(id)
  const now = useNow()

  if (isNotFound(coordinator.error))
    return <Alert color="yellow" title="Coordinator not found">It is not running, or the link is wrong.</Alert>

  return (
    <QueryState query={coordinator}>
      {({ summary, sessions, users }) => (
        <Stack>
          <Group justify="space-between" align="baseline">
            <div>
              <Anchor component={Link} to="/" size="sm">← Overview</Anchor>
              <Title order={2}>{summary.name}</Title>
              <Text size="sm" c="dimmed">{summary.id} · {summary.ip}:{summary.port}</Text>
            </div>
            <Group gap="sm">
              <StateBadge state={summary.state} />
              <Button component={Link} to="roles" variant="default" size="xs">Roles</Button>
              <Button component={Link} to="settings" variant="default" size="xs">Settings</Button>
            </Group>
          </Group>

          <SimpleGrid cols={{ base: 2, sm: 4 }}>
            <StatCard label="Sessions" value={summary.sessionsCount} />
            <StatCard label="Active sessions" value={summary.activeSessionsCount} />
            <StatCard label="Connected users" value={summary.connectedUsersCount} />
            <StatCard label="Uptime" value={formatSince(summary.startedAtUtc, now)} />
          </SimpleGrid>

          <Title order={3}>Sessions</Title>
          <SessionsTable sessions={sessions} />

          <Title order={3}>Connected users</Title>
          <UsersTable users={users} />
        </Stack>
      )}
    </QueryState>
  )
}

function SessionsTable({ sessions }: { sessions: HybridSession[] }) {
  if (sessions.length === 0)
    return <Text c="dimmed">No sessions.</Text>

  return (
    <Table.ScrollContainer minWidth={640}>
      <Table striped>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>ID</Table.Th>
            <Table.Th>Name</Table.Th>
            <Table.Th>Host</Table.Th>
            <Table.Th>Engine</Table.Th>
            <Table.Th ta="right">Peers</Table.Th>
            <Table.Th>Members</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {sessions.map(session => (
            <Table.Tr key={session.id}>
              <Table.Td>{session.id}</Table.Td>
              <Table.Td>
                {session.name}
                {session.isHostMustApprove && <Badge ml="xs" size="xs" variant="outline">approval</Badge>}
                {session.state === 'WaitingForSfuNode' && <Badge ml="xs" size="xs" color="orange">waiting for SFU node</Badge>}
              </Table.Td>
              <Table.Td>#{session.hostId}</Table.Td>
              <Table.Td>{session.engineType}</Table.Td>
              <Table.Td ta="right">{session.peers.length} / {session.capacity ?? '∞'}</Table.Td>
              <Table.Td>
                <Group gap={4}>
                  {session.peers.map(peer => <PeerBadge key={peer.userId} peer={peer} />)}
                </Group>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  )
}

function PeerBadge({ peer }: { peer: Peer }) {
  const color = peer.state === 'Connected' && peer.isTransportConnected ? 'green' : 'yellow'
  const details = [peer.state, peer.isApproved ? 'approved' : 'not approved', peer.isMicrophoneMuted ? 'muted' : null, peer.isSoundMuted ? 'deafened' : null]
    .filter(Boolean)
    .join(' · ')

  return (
    <Tooltip label={details}>
      <Badge color={color} variant="light">#{peer.userId}{peer.isMicrophoneMuted ? ' 🔇' : ''}{peer.isSoundMuted ? ' 🎧' : ''}</Badge>
    </Tooltip>
  )
}

function UsersTable({ users }: { users: ConnectedUser[] }) {
  if (users.length === 0)
    return <Text c="dimmed">Nobody is connected.</Text>

  return (
    <Table.ScrollContainer minWidth={520}>
      <Table striped>
        <Table.Thead>
          <Table.Tr>
            <Table.Th>ID</Table.Th>
            <Table.Th>Username</Table.Th>
            <Table.Th>Display name</Table.Th>
            <Table.Th>Status</Table.Th>
            <Table.Th>Connected at</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {users.map(user => (
            <Table.Tr key={user.id}>
              <Table.Td>{user.id}</Table.Td>
              <Table.Td>{user.username}</Table.Td>
              <Table.Td>{user.displayName ?? '—'}</Table.Td>
              <Table.Td>{user.status}</Table.Td>
              <Table.Td>{formatDateTime(user.connectedAtUtc)}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  )
}
