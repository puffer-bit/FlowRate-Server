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
        <Stack gap="xl">
          <Group justify="space-between" align="baseline">
            <div>
              <Anchor component={Link} to="/coordinators" size="sm">← Coordinators</Anchor>
              <Title order={2}>{summary.name}</Title>
              <Text size="sm" c="dimmed">{summary.id} · {summary.ip}:{summary.port}</Text>
            </div>
            <Group gap="sm">
              <StateBadge state={summary.state} />
              <Button component={Link} to="roles" variant="default" size="xs">Roles</Button>
              <Button component={Link} to="settings" variant="default" size="xs">Settings</Button>
            </Group>
          </Group>

          {summary.state === 'Failed' && <Alert color="red" title="Coordinator could not start">
            Its settings are saved. Check the server logs for the cause, then open <Anchor component={Link} to="settings">Settings</Anchor> and save corrected settings to retry. Other coordinators continue running.
          </Alert>}
          <SimpleGrid cols={{ base: 2, sm: 4 }}>
            <StatCard label="Sessions" value={summary.sessionsCount} icon="server" hint="Available on this coordinator" />
            <StatCard label="Active sessions" value={summary.activeSessionsCount} icon="activity" hint="Currently in progress" />
            <StatCard label="Connected users" value={summary.connectedUsersCount} icon="users" hint="Currently connected" />
            <StatCard label="Uptime" value={summary.state === 'Online' ? formatSince(summary.startedAtUtc, now) : '—'} icon="clock" hint="Since the coordinator started" />
          </SimpleGrid>

          <section className="data-section"><div className="section-heading"><div><Title order={3}>Sessions</Title><Text size="sm" c="dimmed" mt={6}>Live rooms, media engines and participating peers.</Text></div><Badge color="gray" variant="light">{sessions.length}</Badge></div><SessionsTable sessions={sessions} /></section>

          <section className="data-section"><div className="section-heading"><div><Title order={3}>Connected users</Title><Text size="sm" c="dimmed" mt={6}>People connected to this coordinator right now.</Text></div><Badge color="gray" variant="light">{users.length}</Badge></div><UsersTable users={users} /></section>
        </Stack>
      )}
    </QueryState>
  )
}

function SessionsTable({ sessions }: { sessions: HybridSession[] }) {
  if (sessions.length === 0)
    return <div className="empty-state"><Text fw={600}>No active sessions</Text><Text c="dimmed" size="sm">Sessions will appear here when they are created.</Text></div>

  return (
    <Table.ScrollContainer minWidth={640}>
      <Table>
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
                {session.state === 'WaitingForSFUNode' && <Badge ml="xs" size="xs" color="orange">waiting for SFU node</Badge>}
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
    return <div className="empty-state"><Text fw={600}>No users connected</Text><Text c="dimmed" size="sm">Connected users will appear here automatically.</Text></div>

  return (
    <Table.ScrollContainer minWidth={520}>
      <Table>
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
