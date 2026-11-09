import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Anchor, Badge, Button, Group, NumberInput, Select, Stack, Table, Text, Textarea, TextInput, Title } from '@mantine/core'
import { Link, useParams } from 'react-router'
import { createBan, getBans, revokeBan, type CreateGlobalBan } from '../api/client'

export function BansPage() {
  const { id = '' } = useParams()
  const client = useQueryClient()
  const key = ['bans', id]
  const bans = useQuery({ queryKey: key, queryFn: () => getBans(id), refetchInterval: 30_000 })
  const [userId, setUserId] = useState<string | number>('')
  const [kind, setKind] = useState<'Account' | 'Ip'>('Account')
  const [address, setAddress] = useState('')
  const [reason, setReason] = useState('')
  const [duration, setDuration] = useState('0')
  const refresh = () => { void client.invalidateQueries({ queryKey: key }) }
  const create = useMutation({ mutationFn: (value: CreateGlobalBan) => createBan(id, value), onSuccess: () => { setReason(''); refresh() } })
  const revoke = useMutation({ mutationFn: (banId: string) => revokeBan(id, banId), onSuccess: refresh })
  const error = bans.error ?? create.error ?? revoke.error
  return <Stack gap="lg">
    <Anchor component={Link} to={`/coordinators/${id}`}>← Coordinator</Anchor>
    <Title order={2}>Global bans</Title>
    {error && <Alert color="red">{error.message}</Alert>}
    <Group align="end">
      <NumberInput label="User ID" min={1} value={userId} onChange={setUserId} />
      <Select label="Ban by" data={[{ value: 'Account', label: 'Account' }, { value: 'Ip', label: 'IP address' }]} value={kind} onChange={value => setKind(value === 'Ip' ? 'Ip' : 'Account')} />
      <Select label="Duration" data={[{ value: '0', label: 'Permanent' }, { value: '1', label: '1 hour' }, { value: '24', label: '1 day' }, { value: '168', label: '7 days' }, { value: '720', label: '30 days' }]} value={duration} onChange={value => setDuration(value ?? '0')} />
    </Group>
    {kind === 'Ip' && <TextInput label="IP address" description="Blocks everyone on this address. Leave empty to use the account’s last connection IP." value={address} onChange={event => setAddress(event.currentTarget.value)} />}
    <Textarea label="Reason (optional)" maxLength={1000} value={reason} onChange={event => setReason(event.currentTarget.value)} />
    <Button loading={create.isPending} disabled={!userId || revoke.isPending} onClick={() => create.mutate({ userId: Number(userId), kind, reason: reason.trim(), address: kind === 'Ip' && address.trim() ? address.trim() : undefined, expiresAt: duration === '0' ? undefined : new Date(Date.now() + Number(duration) * 3_600_000).toISOString() })}>Ban</Button>
    <Title order={3}>Active bans</Title>
    <Table.ScrollContainer minWidth={700}><Table><Table.Thead><Table.Tr><Table.Th>Target</Table.Th><Table.Th>Reason</Table.Th><Table.Th>Created</Table.Th><Table.Th>Status</Table.Th><Table.Th /></Table.Tr></Table.Thead><Table.Tbody>
      {bans.data?.map(ban => {
        const expired = ban.expiresAt != null && Date.parse(ban.expiresAt) <= Date.now()
        const active = !expired
        return <Table.Tr key={ban.id}>
          <Table.Td>{ban.kind === 'Ip' ? `IP: ${ban.address}` : `Account #${ban.userId}`}</Table.Td>
          <Table.Td><Text style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{ban.reason}</Text></Table.Td>
          <Table.Td>{new Date(ban.createdAt).toLocaleString()} · #{ban.createdBy}</Table.Td>
          <Table.Td><Badge color={active ? 'red' : 'gray'}>{expired ? 'Expired' : 'Active'}</Badge><Text size="xs">{ban.expiresAt ? `Until ${new Date(ban.expiresAt).toLocaleString()}` : 'Permanent'}</Text></Table.Td>
          <Table.Td>{active && <Button size="xs" variant="default" disabled={create.isPending} loading={revoke.isPending} onClick={() => revoke.mutate(ban.id)}>Revoke</Button>}</Table.Td>
        </Table.Tr>
      })}
    </Table.Tbody></Table></Table.ScrollContainer>
  </Stack>
}
