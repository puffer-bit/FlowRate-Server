// The browser side of adding a passkey. The server speaks the WebAuthn JSON forms, binary values in base64url;
// navigator.credentials wants buffers, so they are converted here both ways.

type Json = Record<string, unknown>

const fromBase64Url = (value: string): ArrayBuffer => {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(value.length / 4) * 4, '=')
  const binary = atob(base64)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++)
    bytes[i] = binary.charCodeAt(i)
  return bytes.buffer
}

const toBase64Url = (buffer: ArrayBuffer): string => {
  let binary = ''
  for (const byte of new Uint8Array(buffer))
    binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function toCreationOptions(options: Json): PublicKeyCredentialCreationOptions {
  const user = options.user as Json
  const exclude = (options.excludeCredentials as Json[] | undefined) ?? []
  return {
    ...(options as unknown as PublicKeyCredentialCreationOptions),
    challenge: fromBase64Url(options.challenge as string),
    user: { ...(user as unknown as PublicKeyCredentialUserEntity), id: fromBase64Url(user.id as string) },
    excludeCredentials: exclude.map(credential => ({
      ...(credential as unknown as PublicKeyCredentialDescriptor),
      id: fromBase64Url(credential.id as string),
    })),
  }
}

/** Why a passkey could not be made, in words for the panel. */
export class PasskeyError extends Error {}

/** Lets the browser make a credential for the options of the server; answers with its JSON form. */
export async function createPasskey(options: Json): Promise<Json> {
  if (!window.isSecureContext || !navigator.credentials || !window.PublicKeyCredential)
    throw new PasskeyError('This browser cannot add passkeys here: open the panel over https in a current browser.')

  let credential: Credential | null
  try {
    credential = await navigator.credentials.create({ publicKey: toCreationOptions(options) })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'NotAllowedError')
      throw new PasskeyError('The passkey was not made: it was cancelled or took too long.')
    if (error instanceof DOMException && error.name === 'InvalidStateError')
      throw new PasskeyError('This authenticator holds a passkey of the user already.')
    if (error instanceof DOMException && error.name === 'SecurityError')
      throw new PasskeyError(`Passkeys of this server belong to ${(options.rp as Json | undefined)?.id ?? 'another domain'}; open the panel by that name.`)
    throw error
  }
  if (!(credential instanceof PublicKeyCredential))
    throw new PasskeyError('The browser gave no passkey.')

  const response = credential.response as AuthenticatorAttestationResponse
  return {
    id: credential.id,
    rawId: toBase64Url(credential.rawId),
    type: credential.type,
    response: {
      attestationObject: toBase64Url(response.attestationObject),
      clientDataJSON: toBase64Url(response.clientDataJSON),
      transports: response.getTransports?.() ?? [],
    },
    clientExtensionResults: credential.getClientExtensionResults(),
  }
}
