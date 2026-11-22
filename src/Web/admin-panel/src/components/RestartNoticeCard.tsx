import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Checkbox, Group, NumberInput, Select, Stack, Text, Textarea, Title } from '@mantine/core'
import { announceRestart, cancelRestartNotice, getRestartNotice, restartNow, type AnnounceRestart, type RestartReason } from '../api/client'

const reasons: { value: RestartReason, label: string }[] = [
  { value: 'Restart', label: 'Restart' },
  { value: 'Update', label: 'Update' },
  { value: 'Shutdown', label: 'Shutdown' },
]

/**
 * Warns everyone connected before the server goes down. The warning only tells: the server stops when it is stopped
 * (docker compose restart, pull and up), or by itself at the time for a restart that asks for it.
 */
export function RestartNoticeCard() {
  const client = useQueryClient()
  const key = ['restart-notice']
  const notice = useQuery({ queryKey: key, queryFn: getRestartNotice, refetchInterval: 30_000 })
  const [reason, setReason] = useState<RestartReason>('Update')
  const [minutes, setMinutes] = useState<string | number>(5)
  const [message, setMessage] = useState('')
  const [restartsAtEnd, setRestartsAtEnd] = useState(false)
  const refresh = () => { void client.invalidateQueries({ queryKey: key }) }
  const announce = useMutation({ mutationFn: (value: AnnounceRestart) => announceRestart(value), onSuccess: refresh })
  const cancel = useMutation({ mutationFn: cancelRestartNotice, onSuccess: refresh })
  const now = useMutation({ mutationFn: () => restartNow(message.trim() || undefined) })
  const error = notice.error ?? announce.error ?? cancel.error ?? now.error
  const current = notice.data

  return (
    <Card withBorder padding="lg">
      <Stack gap="sm">
        <Title order={3}>Restart warning</Title>
        <Text c="dimmed" size="sm">Tells everyone connected that the server goes down soon, and why. Users who connect later see it too.</Text>
        {error && <Alert color="red">{error.message}</Alert>}
        {current
          ? <Group justify="space-between" align="center">
              <Text>
                <b>{current.reason}</b> at {new Date(current.at).toLocaleString()}
                {current.restartsAtEnd && ' — the server restarts itself then'}
                {current.message && <><br /><Text span c="dimmed">{current.message}</Text></>}
              </Text>
              <Button variant="default" loading={cancel.isPending} onClick={() => cancel.mutate()}>Call off</Button>
            </Group>
          : <>
              <Group align="end">
                <Select label="Reason" data={reasons} value={reason} allowDeselect={false}
                  onChange={value => { const next = (value ?? 'Update') as RestartReason; setReason(next); if (next !== 'Restart') setRestartsAtEnd(false) }} />
                <NumberInput label="In minutes" min={1} max={1440} value={minutes} onChange={setMinutes} />
              </Group>
              <Textarea label="Message (optional)" maxLength={500} value={message} onChange={event => setMessage(event.currentTarget.value)} />
              {reason === 'Restart' && <Checkbox label="Restart the server by itself at that time (needs a supervisor such as Docker to start it again)"
                checked={restartsAtEnd} onChange={event => setRestartsAtEnd(event.currentTarget.checked)} />}
              <Group><Button loading={announce.isPending}
                onClick={() => announce.mutate({ reason, delayMinutes: Math.max(1, Number(minutes) || 1), message: message.trim() || undefined, restartsAtEnd })}>Warn users</Button>
                <Button color="red" variant="light" loading={now.isPending} disabled={now.isSuccess}
                  onClick={() => { if (window.confirm('Restart the server now? Everyone is disconnected and reconnects when it is back. It needs a supervisor such as Docker to start again.')) now.mutate() }}>
                  {now.isSuccess ? 'Restarting…' : 'Restart now'}</Button></Group>
            </>}
      </Stack>
    </Card>
  )
}
