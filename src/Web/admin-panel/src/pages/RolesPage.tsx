import {
  ActionIcon,
  Alert,
  Anchor,
  Badge,
  Button,
  Checkbox,
  ColorInput,
  ColorSwatch,
  Group,
  Modal,
  MultiSelect,
  SimpleGrid,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { isConflict, isNotFound, type CoordinatorUser, type Permission, type Role } from '../api/client'
import {
  useCoordinatorUsers,
  useCreateRole,
  useDeleteRole,
  useReorderRoles,
  useRoles,
  useSetUserRole,
  useUpdateRole,
} from '../api/hooks'
import { formatDateTime } from '../format'
import { QueryState } from '../components/QueryState'
import { UserSecurityModal } from '../components/UserSecurityModal'

/** Every single permission, in the order the editor lists them. */
const permissions: { value: Permission, label: string, hint: string }[] = [
  { value: 'Administrator', label: 'Administrator', hint: 'Everything below; role order still applies' },
  { value: 'ViewCoordinatorSettings', label: 'View coordinator settings', hint: 'Open the settings in the client' },
  { value: 'ManageCoordinatorSettings', label: 'Manage coordinator settings', hint: 'Change the settings' },
  { value: 'ManageRoles', label: 'Manage roles', hint: 'Roles below their own highest role' },
  { value: 'CreateSessions', label: 'Create sessions', hint: 'Creators host their sessions' },
  { value: 'ManageSessions', label: 'Manage sessions', hint: 'Change and delete any session, arrange sessions' },
  { value: 'KickMembers', label: 'Kick members', hint: 'From any session; hosts may in their own' },
  { value: 'BanMembers', label: 'Ban members', hint: 'From any session; hosts may in their own' },
  { value: 'ApproveMembers', label: 'Approve members', hint: 'Let users waiting to enter in, or refuse them' },
  { value: 'ManageMessages', label: 'Manage messages', hint: 'Delete messages of others' },
  { value: 'ManageUsers', label: 'Manage users', hint: 'Change names and avatars of others' },
]

const toHex = (color: number | null | undefined) =>
  color == null ? '' : `#${color.toString(16).padStart(6, '0')}`

const fromHex = (hex: string) => (/^#[0-9a-f]{6}$/i.test(hex) ? parseInt(hex.slice(1), 16) : null)

export function RolesPage() {
  const { id = '' } = useParams()
  const roles = useRoles(id)
  const users = useCoordinatorUsers(id)

  if (isNotFound(roles.error))
    return <Alert color="yellow" title="Coordinator not found">The link is wrong.</Alert>

  return (
    <Stack>
      <div>
        <Anchor component={Link} to={`/coordinators/${encodeURIComponent(id)}`} size="sm">← Coordinator</Anchor>
        <Title order={2}>Roles</Title>
        <Text size="sm" c="dimmed">
          Users have the permissions of all their roles and of @everyone. A role manages only roles and users
          below it; the panel may do anything, so it makes the first administrator.
        </Text>
      </div>

      <QueryState query={roles}>
        {data => <RolesTable coordinatorId={id} roles={data} />}
      </QueryState>

      <Title order={3}>Users</Title>
      <QueryState query={users}>
        {userData => roles.data && <UsersTable coordinatorId={id} roles={roles.data} users={userData} />}
      </QueryState>
    </Stack>
  )
}

function RolesTable({ coordinatorId, roles }: { coordinatorId: string, roles: Role[] }) {
  const [editing, setEditing] = useState<Role | 'new' | null>(null)
  const reorder = useReorderRoles(coordinatorId)
  const remove = useDeleteRole(coordinatorId)
  const error = reorder.error ?? remove.error

  // Shown highest first, as they rank; sent lowest first, as the server keeps them
  const ordered = roles.filter(role => !role.isEveryone)
  const highestFirst = [...roles].reverse()

  const move = (role: Role, by: number) => {
    const ids = ordered.map(r => r.id)
    const from = ids.indexOf(role.id)
    const to = from + by
    if (to < 0 || to >= ids.length)
      return
    ;[ids[from], ids[to]] = [ids[to], ids[from]]
    reorder.mutate(ids)
  }

  const busy = reorder.isPending || remove.isPending

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>Roles</Title>
        <Button size="xs" onClick={() => setEditing('new')} disabled={busy}>New role</Button>
      </Group>

      {error && <Alert color="red" title="The change was not made">{error.message}</Alert>}

      <Table.ScrollContainer minWidth={560}>
        <Table striped>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Role</Table.Th>
              <Table.Th>Permissions</Table.Th>
              <Table.Th w={180} />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {highestFirst.map(role => (
              <Table.Tr key={role.id}>
                <Table.Td>
                  <Group gap="xs" wrap="nowrap">
                    <ColorSwatch color={toHex(role.color) || 'transparent'} size={14} />
                    <Text fw={500}>{role.name}</Text>
                  </Group>
                </Table.Td>
                <Table.Td>
                  <PermissionBadges permissions={role.permissions} />
                </Table.Td>
                <Table.Td>
                  <Group gap={4} justify="flex-end" wrap="nowrap">
                    {!role.isEveryone && (
                      <>
                        <ActionIcon variant="default" aria-label="Move up" disabled={busy || role === ordered.at(-1)}
                          onClick={() => move(role, 1)}>↑</ActionIcon>
                        <ActionIcon variant="default" aria-label="Move down" disabled={busy || role === ordered[0]}
                          onClick={() => move(role, -1)}>↓</ActionIcon>
                      </>
                    )}
                    <Button size="xs" variant="default" disabled={busy} onClick={() => setEditing(role)}>Edit</Button>
                    {!role.isEveryone && (
                      <Button size="xs" variant="subtle" color="red" disabled={busy}
                        loading={remove.isPending && remove.variables === role.id}
                        onClick={() => {
                          if (confirm(`Delete the role ${role.name}? Everyone who has it loses it.`))
                            remove.mutate(role.id)
                        }}>
                        Delete
                      </Button>
                    )}
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      {editing && (
        <RoleEditor
          coordinatorId={coordinatorId}
          role={editing === 'new' ? null : editing}
          current={editing === 'new' ? null : roles.find(role => role.id === editing.id) ?? null}
          onClose={() => setEditing(null)}
        />
      )}
    </Stack>
  )
}

function PermissionBadges({ permissions: granted }: { permissions: Permission[] }) {
  if (granted.length === 0)
    return <Text size="sm" c="dimmed">None</Text>
  if (granted.includes('Administrator'))
    return <Badge color="red" variant="light">Administrator</Badge>

  return (
    <Group gap={4}>
      {permissions.filter(p => granted.includes(p.value)).map(p => (
        <Badge key={p.value} variant="light" size="sm">{p.label}</Badge>
      ))}
    </Group>
  )
}

/**
 * Creates a role, or changes one from the values it had when the editor opened. When it was changed elsewhere
 * meanwhile the form keeps its values and says so.
 */
function RoleEditor({ coordinatorId, role, current, onClose }: {
  coordinatorId: string
  role: Role | null
  current: Role | null
  onClose: () => void
}) {
  const [name, setName] = useState(role?.name ?? '')
  const [color, setColor] = useState(toHex(role?.color))
  const [granted, setGranted] = useState<string[]>(role?.permissions ?? [])
  const create = useCreateRole(coordinatorId)
  const update = useUpdateRole(coordinatorId)
  const mutation = role ? update : create

  const conflict = isConflict(mutation.error)
  const changedElsewhere = role != null && current != null && current.revision !== role.revision
  const isEveryone = role?.isEveryone ?? false

  const save = () => {
    const change = { name: name.trim(), color: fromHex(color), permissions: granted as Permission[] }
    if (role)
      update.mutate({ roleId: role.id, role: { ...change, expectedRevision: role.revision } }, { onSuccess: onClose })
    else
      create.mutate(change, { onSuccess: onClose })
  }

  return (
    <Modal opened onClose={onClose} title={role ? `Edit ${role.name}` : 'New role'} size="lg">
      <Stack>
        {(conflict || changedElsewhere) && (
          <Alert color="yellow" title="The role was changed elsewhere">
            Close the editor and open it again to see the changes.
          </Alert>
        )}
        {mutation.error && !conflict && <Alert color="red" title="Not saved">{mutation.error.message}</Alert>}

        <fieldset disabled={mutation.isPending} style={{ border: 0, padding: 0, margin: 0 }}>
          <Stack>
            <TextInput label="Name" value={name} onChange={e => setName(e.currentTarget.value)} disabled={isEveryone}
              maxLength={32} required />
            <ColorInput label="Color" value={color} onChange={setColor} disabled={isEveryone} format="hex"
              placeholder="No color" />
            <Checkbox.Group label="Permissions" value={granted} onChange={setGranted}>
              <SimpleGrid cols={{ base: 1, sm: 2 }} mt="xs">
                {permissions.map(p => (
                  <Tooltip key={p.value} label={p.hint} openDelay={400}>
                    <Checkbox value={p.value} label={p.label} />
                  </Tooltip>
                ))}
              </SimpleGrid>
            </Checkbox.Group>
          </Stack>
        </fieldset>

        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>Cancel</Button>
          <Button onClick={save} loading={mutation.isPending} disabled={conflict || changedElsewhere}>Save</Button>
        </Group>
      </Stack>
    </Modal>
  )
}

function UsersTable({ coordinatorId, roles, users }: {
  coordinatorId: string
  roles: Role[]
  users: CoordinatorUser[]
}) {
  const setUserRole = useSetUserRole(coordinatorId)
  const [securityOf, setSecurityOf] = useState<CoordinatorUser | null>(null)
  const assignable = [...roles].reverse().filter(role => !role.isEveryone)
  const options = assignable.map(role => ({ value: String(role.id), label: role.name }))

  if (users.length === 0)
    return <Text c="dimmed">No user has connected yet.</Text>

  const change = (user: CoordinatorUser, selected: string[]) => {
    const next = selected.map(Number)
    const added = next.find(roleId => !user.roleIds.includes(roleId))
    const removed = user.roleIds.find(roleId => !next.includes(roleId))
    if (added !== undefined)
      setUserRole.mutate({ userId: user.id, roleId: added, has: true })
    else if (removed !== undefined)
      setUserRole.mutate({ userId: user.id, roleId: removed, has: false })
  }

  return (
    <Stack>
      {setUserRole.error && <Alert color="red" title="The role was not changed">{setUserRole.error.message}</Alert>}
      <Table.ScrollContainer minWidth={640}>
        <Table striped>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>ID</Table.Th>
              <Table.Th>Username</Table.Th>
              <Table.Th>Last connected</Table.Th>
              <Table.Th>Roles</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {users.map(user => (
              <Table.Tr key={user.id}>
                <Table.Td>{user.id}</Table.Td>
                <Table.Td>
                  {user.username}
                  {user.displayName && <Text span c="dimmed" size="sm"> · {user.displayName}</Text>}
                  {user.state === 'Banned' && <Badge ml="xs" size="xs" color="red">banned</Badge>}
                </Table.Td>
                <Table.Td>{formatDateTime(user.lastConnectionAtUtc)}</Table.Td>
                <Table.Td miw={260}>
                  <MultiSelect
                    data={options}
                    value={user.roleIds.map(String)}
                    onChange={selected => change(user, selected)}
                    disabled={setUserRole.isPending}
                    placeholder={user.roleIds.length === 0 ? '@everyone only' : undefined}
                    aria-label={`Roles of ${user.username}`}
                    size="xs"
                  />
                </Table.Td>
                <Table.Td ta="right">
                  <Button size="xs" variant="default" onClick={() => setSecurityOf(user)}>Login</Button>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      {securityOf && (
        <UserSecurityModal coordinatorId={coordinatorId} user={securityOf} onClose={() => setSecurityOf(null)} />
      )}
    </Stack>
  )
}
