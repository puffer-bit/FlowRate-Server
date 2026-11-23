import { Alert, Badge, Button, Code, Divider, Group, Image, Modal, PasswordInput, PinInput, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import QRCode from 'qrcode'
import { useEffect, useState } from 'react'
import type { CoordinatorUser, LoginCode, TwoFactorOffer, UserDevice, UserSecurity } from '../api/client'
import {
  useAddPasskey,
  useConfirmTwoFactor,
  useCreateLoginCode,
  useRevokeUserDevice,
  useUserDevices,
  useDeletePasskey,
  useDisableTwoFactor,
  useOfferTwoFactor,
  useSetUserPassword,
  useUserSecurity,
} from '../api/hooks'
import { formatDateTime } from '../format'
import { QueryState } from './QueryState'

const passwordMinLength = 8

/**
 * How a user logs in: the devices of the account, and the password, one-time codes and passkeys a new device can be
 * added with by username. The panel takes them away when a user lost a device, and gives out a one-time login code
 * when the user has nothing else left.
 */
export function UserSecurityModal({ coordinatorId, user, onClose }: {
  coordinatorId: string
  user: CoordinatorUser
  onClose: () => void
}) {
  const security = useUserSecurity(coordinatorId, user.id)

  return (
    <Modal opened onClose={onClose} title={`Login of ${user.username}`} size="lg">
      <QueryState query={security}>
        {data => (
          <Stack>
            <DevicesSection coordinatorId={coordinatorId} user={user} />
            <Divider />
            <Text size="sm" c="dimmed">
              {data.hasPassword || data.isTwoFactorEnabled || data.passkeys.length > 0
                ? 'A new device can be added by username with what is set below. A passkey alone is enough.'
                : 'Nothing is set: a new device is added only with the recovery phrase, another device or a login code.'}
            </Text>
            <PasswordSection coordinatorId={coordinatorId} userId={user.id} security={data} />
            <Divider />
            <TwoFactorSection coordinatorId={coordinatorId} user={user} security={data} />
            <Divider />
            <PasskeysSection coordinatorId={coordinatorId} userId={user.id} security={data} />
          </Stack>
        )}
      </QueryState>
    </Modal>
  )
}

const addedByLabels: Record<UserDevice['addedBy'], string> = {
  Registration: 'registered the account',
  Recovery: 'recovery phrase',
  Factors: 'username and password or code',
  LoginCode: 'login code',
  DeviceLink: 'approved by another device',
  Legacy: 'moved over from the identity',
}

function DevicesSection({ coordinatorId, user }: { coordinatorId: string, user: CoordinatorUser }) {
  const devices = useUserDevices(coordinatorId, user.id)
  const revoke = useRevokeUserDevice(coordinatorId, user.id)
  const createCode = useCreateLoginCode(coordinatorId, user.id)
  const [code, setCode] = useState<LoginCode | null>(null)
  const error = revoke.error ?? createCode.error

  return (
    <Stack gap="xs">
      <Group gap="xs">
        <Title order={5}>Devices</Title>
        {devices.data && <StateBadge on={devices.data.length > 0} label={String(devices.data.length)} />}
      </Group>
      {error && <Alert color="red">{error.message}</Alert>}
      <QueryState query={devices}>
        {list => list.length === 0
          ? <Text size="sm">No devices: the user logs in again with the recovery phrase, a factor or a login code.</Text>
          : (
            <Table>
              <Table.Tbody>
                {list.map(device => (
                  <Table.Tr key={device.id}>
                    <Table.Td>
                      <Group gap={6}>
                        <Text size="sm">{device.name}</Text>
                        {device.isOnline && <Badge size="xs" color="green" variant="light">online</Badge>}
                        {device.isTemporary && <Badge size="xs" color="gray" variant="light">temporary</Badge>}
                      </Group>
                      <Text size="xs" c="dimmed">{device.platform} · FlowRate {device.appVersion}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="xs" c="dimmed">Added {formatDateTime(device.createdAtUtc)}: {addedByLabels[device.addedBy]}</Text>
                      <Text size="xs" c="dimmed">
                        Last login {formatDateTime(device.lastUsedAtUtc)}{device.lastAddress ? ` from ${device.lastAddress}` : ''}
                      </Text>
                    </Table.Td>
                    <Table.Td ta="right">
                      <Button size="xs" variant="subtle" color="red"
                        loading={revoke.isPending && revoke.variables === device.id}
                        onClick={() => {
                          if (window.confirm(`Remove the device ${device.name}? It is logged out and has to be added again.`))
                            revoke.mutate(device.id)
                        }}>
                        Remove
                      </Button>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}
      </QueryState>
      {code
        ? (
          <Alert color="blue" title="One-time login code">
            <Stack gap={4}>
              <Code fz="lg">{code.code}</Code>
              <Text size="sm">
                Give it to {user.username} yourself. On the new device: Log in to an existing account → Login code.
                It adds one device and works until {formatDateTime(code.expiresAtUtc)}. It is not shown again.
              </Text>
            </Stack>
          </Alert>
        )
        : (
          <Group justify="space-between">
            <Text size="sm">For a user who lost every device and the recovery phrase.</Text>
            <Button variant="default" loading={createCode.isPending}
              onClick={() => createCode.mutate(undefined, { onSuccess: setCode })}>
              Give out a login code
            </Button>
          </Group>
        )}
    </Stack>
  )
}

const StateBadge = ({ on, label }: { on: boolean, label: string }) =>
  <Badge size="sm" variant="light" color={on ? 'green' : 'gray'}>{label}</Badge>

function PasswordSection({ coordinatorId, userId, security }: {
  coordinatorId: string
  userId: number
  security: UserSecurity
}) {
  const [password, setPassword] = useState('')
  const change = useSetUserPassword(coordinatorId, userId)
  const tooShort = password.length > 0 && password.length < passwordMinLength

  return (
    <Stack gap="xs">
      <Group gap="xs">
        <Title order={5}>Password</Title>
        <StateBadge on={security.hasPassword} label={security.hasPassword ? 'set' : 'not set'} />
      </Group>
      {change.error && <Alert color="red">{change.error.message}</Alert>}
      <Group align="flex-start" gap="xs">
        <PasswordInput flex={1} placeholder={security.hasPassword ? 'New password' : 'Password'} value={password}
          onChange={e => setPassword(e.currentTarget.value)} maxLength={128} autoComplete="new-password"
          error={tooShort ? `At least ${passwordMinLength} characters` : undefined} aria-label="Password" />
        <Button disabled={password.length < passwordMinLength} loading={change.isPending && change.variables !== null}
          onClick={() => change.mutate(password, { onSuccess: () => setPassword('') })}>
          {security.hasPassword ? 'Change' : 'Set'}
        </Button>
        {security.hasPassword && (
          <Button variant="subtle" color="red" loading={change.isPending && change.variables === null}
            onClick={() => change.mutate(null)}>
            Remove
          </Button>
        )}
      </Group>
    </Stack>
  )
}

function TwoFactorSection({ coordinatorId, user, security }: {
  coordinatorId: string
  user: CoordinatorUser
  security: UserSecurity
}) {
  const [offer, setOffer] = useState<TwoFactorOffer | null>(null)
  const [code, setCode] = useState('')
  const start = useOfferTwoFactor(coordinatorId, user.id)
  const turnOn = useConfirmTwoFactor(coordinatorId, user.id)
  const disable = useDisableTwoFactor(coordinatorId, user.id)
  const error = start.error ?? turnOn.error ?? disable.error

  return (
    <Stack gap="xs">
      <Group gap="xs">
        <Title order={5}>One-time codes</Title>
        <StateBadge on={security.isTwoFactorEnabled} label={security.isTwoFactorEnabled ? 'on' : 'off'} />
      </Group>
      {error && <Alert color="red">{error.message}</Alert>}

      {security.isTwoFactorEnabled && (
        <Group justify="space-between">
          <Text size="sm">Every login asks for a code from the user&apos;s authenticator app.</Text>
          <Button variant="subtle" color="red" loading={disable.isPending}
            onClick={() => {
              if (window.confirm(`Turn one-time codes off for ${user.username}?`))
                disable.mutate(undefined, { onSuccess: () => setOffer(null) })
            }}>
            Turn off
          </Button>
        </Group>
      )}

      {!security.isTwoFactorEnabled && !offer && (
        <Group justify="space-between">
          <Text size="sm">Codes from an authenticator app, such as Aegis, Google Authenticator or 1Password.</Text>
          <Button variant="default" loading={start.isPending}
            onClick={() => start.mutate(undefined, { onSuccess: setOffer })}>
            Set up
          </Button>
        </Group>
      )}

      {!security.isTwoFactorEnabled && offer && (
        <Stack gap="xs">
          <Text size="sm">
            Scan the code with the user&apos;s authenticator app, or type the secret into it, then enter the code
            it shows. Codes are asked for only after this.
          </Text>
          <Group align="flex-start" wrap="nowrap">
            <QrCode value={offer.uri} />
            <Stack gap={4}>
              <Text size="xs" c="dimmed">Secret</Text>
              <Code>{offer.secret.match(/.{1,4}/g)?.join(' ')}</Code>
            </Stack>
          </Group>
          <Group>
            <PinInput length={6} type="number" oneTimeCode value={code} onChange={setCode} aria-label="Code" />
            <Button disabled={code.length !== 6} loading={turnOn.isPending}
              onClick={() => turnOn.mutate(code, {
                onSuccess: () => {
                  setOffer(null)
                  setCode('')
                },
              })}>
              Turn on
            </Button>
            <Button variant="subtle" onClick={() => setOffer(null)}>Cancel</Button>
          </Group>
        </Stack>
      )}
    </Stack>
  )
}

function QrCode({ value }: { value: string }) {
  const [src, setSrc] = useState<string>()
  useEffect(() => {
    let current = true
    void QRCode.toDataURL(value, { margin: 1, width: 160 }).then(url => current && setSrc(url))
    return () => {
      current = false
    }
  }, [value])

  return <Image src={src} w={160} h={160} alt="QR code for the authenticator app" bg="white" radius="sm" />
}

function PasskeysSection({ coordinatorId, userId, security }: {
  coordinatorId: string
  userId: number
  security: UserSecurity
}) {
  const [label, setLabel] = useState('')
  const add = useAddPasskey(coordinatorId, userId)
  const remove = useDeletePasskey(coordinatorId, userId)
  const error = add.error ?? remove.error

  return (
    <Stack gap="xs">
      <Group gap="xs">
        <Title order={5}>Passkeys</Title>
        <StateBadge on={security.passkeys.length > 0} label={String(security.passkeys.length)} />
      </Group>
      {error && <Alert color="red">{error.message}</Alert>}

      {security.passkeys.length > 0 && (
        <Table>
          <Table.Tbody>
            {security.passkeys.map(passkey => (
              <Table.Tr key={passkey.id}>
                <Table.Td>{passkey.label}</Table.Td>
                <Table.Td>
                  <Text size="xs" c="dimmed">Added {formatDateTime(passkey.createdAtUtc)}</Text>
                  <Text size="xs" c="dimmed">
                    {passkey.lastUsedAtUtc ? `Last used ${formatDateTime(passkey.lastUsedAtUtc)}` : 'Never used'}
                  </Text>
                </Table.Td>
                <Table.Td ta="right">
                  <Button size="xs" variant="subtle" color="red"
                    loading={remove.isPending && remove.variables === passkey.id}
                    onClick={() => {
                      if (window.confirm(`Remove the passkey ${passkey.label}? It no longer logs the user in.`))
                        remove.mutate(passkey.id)
                    }}>
                    Remove
                  </Button>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}

      {security.passkeysAvailable
        ? (
          <>
            <Text size="sm">
              A security key, or the passkey of a phone or computer. The browser asks for it on the device the panel
              is open on.
            </Text>
            <Group align="flex-start" gap="xs">
              <TextInput flex={1} placeholder="Name, such as YubiKey" value={label} maxLength={64}
                onChange={e => setLabel(e.currentTarget.value)} aria-label="Passkey name" />
              <Button variant="default" loading={add.isPending}
                onClick={() => add.mutate(label.trim() || null, { onSuccess: () => setLabel('') })}>
                Add passkey
              </Button>
            </Group>
          </>
        )
        : (
          <Alert color="gray">
            Passkeys are not set up on this server. Set <Code>Fido2:RelyingPartyId</Code> (<Code>PASSKEY_DOMAIN</Code>
            {' '}in .env) to the domain users reach it by, and open the panel by that name.
          </Alert>
        )}
    </Stack>
  )
}
