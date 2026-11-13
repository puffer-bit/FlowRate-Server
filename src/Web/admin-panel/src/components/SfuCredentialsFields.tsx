import { Alert, PasswordInput, TextInput } from '@mantine/core'

export function SfuCredentialsFields({ username, password, onUsernameChange, onPasswordChange, errors, isSecured }: {
  username: string, password: string, onUsernameChange: (value: string) => void, onPasswordChange: (value: string) => void,
  errors: Record<string, string[]>, isSecured: boolean,
}) {
  const error = (field: string) => errors[field]?.join(' ') ?? errors[field.toLowerCase()]?.join(' ')
  return <>
    <TextInput label="SFU username" description="Leave both fields empty for a node without authentication" maxLength={256}
      value={username} onChange={event => onUsernameChange(event.currentTarget.value)} error={error('Username')} />
    <PasswordInput label="SFU password" maxLength={4096} value={password} autoComplete="new-password"
      onChange={event => onPasswordChange(event.currentTarget.value)} error={error('Password')} />
    {!isSecured && (username || password) && <Alert color="yellow">Credentials are sent without encryption. Use a trusted private network or TLS termination.</Alert>}
  </>
}
