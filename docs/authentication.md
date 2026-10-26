# Logging in: password, one-time codes and passkeys

A user is identified by the identity the server gave them on their first connection. Besides that identity, a
user can be made to prove who they are with:

| Factor            | What the user has                                                        |
|-------------------|--------------------------------------------------------------------------|
| Password          | 8–128 characters, stored as a PBKDF2 hash.                               |
| One-time codes    | TOTP (RFC 6238): 6 digits, 30 seconds, SHA-1, from an authenticator app. |
| Passkeys (FIDO2)  | Security keys, or passkeys of a phone or computer. User verification (PIN, biometrics) is required. |

A user with none of them logs in with the identity alone, as before. Users set them up from the client (see
[Setting up from the client](#setting-up-from-the-client)); the control panel can do it too, under
**Roles → Users → Login**, and is how they are taken away when a user loses a device.

## The login exchange

```
client                                                       server
  │  UserAuthRequest(ServerPassword, UserIdentity) ───────────► │
  │                                                             │  nothing set: attached
  │ ◄──────────────── UserAuthResponse(Success, UserDTO)        │
  │                                                             │
  │                                                             │  password / codes / passkeys set
  │ ◄──────── UserAuthResponse(SecondStageRequeried,            │
  │             ServerMessage = "Password,TwoFactor,FIDO2",     │
  │             FIDO2LoginOptionsDTO)                           │
  │                                                             │
  │  TwoFactorUserAuthRequest(Password, TwoFactorCode) ───────► │  either one
  │  FIDO2UserAuthRequest(AssertionResponse) ─────────────────► │
  │ ◄──────── TwoFactorUserAuthResponse / FIDO2UserAuthResponse │
  │             (Success, UserDTO) or (AuthFailed)              │
```

- `ServerMessage` of the `SecondStageRequeried` answer lists what the user can prove, as names of
  `SecondStageAuthType`, comma separated: `Password`, `TwoFactor`, `FIDO2`.
- `TwoFactorUserAuthRequest` carries the password and the code together; each is checked when the user has it.
  A user who has passkeys alone cannot use it.
- A passkey alone is enough: it already proves possession and, through user verification, the user.
- A code is good for one login. A wrong password does not use the code up.
- The second stage has three tries and two minutes. A wrong answer gets `AuthFailed` and the connection stays
  open; the third closes it, as does the authentication timeout of the connection.
- Second-stage requests count against the logins per IP rate limit (`RateLimits.AuthenticationPerIp`), which
  is what keeps codes from being guessed.
- A banned user is refused (`Banned`) at the first stage, without a second one; a user banned between the two
  stages is refused when the second one holds.

## Passkeys

`FIDO2LoginOptionsDTO` holds what `navigator.credentials.get` (or a platform WebAuthn API) needs:

| Field                    | Meaning                                                         |
|--------------------------|-----------------------------------------------------------------|
| `Challenge`              | base64url                                                       |
| `RpId`                   | The relying party: the domain passkeys of this server belong to |
| `Timeout`                | Milliseconds                                                    |
| `AllowedCredentials[].Id`| base64url credential ids of the user's passkeys                 |
| `UserVerification`       | Always `required`                                               |

The fields of `FIDO2AssertionDTO` are the parts of the assertion, base64url (plain base64 is accepted too):
`CredentialId` (the raw id), `ClientDataJson`, `AuthenticatorData`, `Signature` and, for discoverable
credentials, `UserHandle`, which is the user id as UTF-8 text.

The server checks the challenge, the origin, the relying party, the signature, user verification and that the
signature counter grows (a counter that does not grow means a cloned key).

### Configuration

Passkeys are off until the relying party is set:

```json
"Fido2": {
  "RelyingPartyId": "chat.example.com",
  "RelyingPartyName": "FlowRate",
  "Origins": []
}
```

With Docker, `PASSKEY_DOMAIN` in `.env` sets `RelyingPartyId`.

- `RelyingPartyId` is the domain users reach the server by, without scheme or port. Passkeys are bound to it:
  changing it makes every registered passkey useless.
- `Origins` are where a ceremony may come from. Empty allows the control panel alone,
  `https://<RelyingPartyId>:<Orchestrator:Port>`; list the panel and every client origin otherwise
  (`Fido2__Origins__0`, `Fido2__Origins__1`... as environment variables).
- To add passkeys in the panel, open it by the relying party name over https. Through an ssh tunnel, point the
  domain at 127.0.0.1 in the hosts file of the computer the browser runs on.
- A user who has passkeys alone cannot log in while passkeys are off; the panel can still remove them.

## Setting up from the client

Once logged in, the client sets up the sender's own account with the `User*` requests of
`Shared.Models.Requests.Coordinator.Security`; each answers with `UserSecurityDTO`, what the user has now.

| Request                       | Proof | Does                                                                  |
|-------------------------------|-------|-----------------------------------------------------------------------|
| `UserSecurityInfoRequest`     |       | Tells what the user has                                               |
| `UserSetPasswordRequest`      | yes   | Sets the password, or removes it with `NewPassword` null              |
| `UserTwoFactorSetupRequest`   | yes   | Offers a secret: `Secret` (Base32) and an `otpauth://` `Uri` for a QR code |
| `UserTwoFactorEnableRequest`  |       | Turns codes on with a code made from the offered secret               |
| `UserTwoFactorDisableRequest` | yes   | Turns codes off                                                       |
| `UserPasskeyOptionsRequest`   | yes   | `PublicKeyCredentialCreationOptionsJSON` for a new passkey            |
| `UserPasskeyRegisterRequest`  |       | The passkey made for those options, as `RegistrationResponseJSON`     |
| `UserPasskeyDeleteRequest`    | yes   | Removes a passkey                                                     |

- The proof (`UserSecurityProofDTO`) is the current password and one-time code, each when the user has it; a
  first password needs none. A missing or wrong one is answered `ProofRequired`. So an open session alone cannot
  change how the account logs in, nor lock its owner out. Enabling codes and registering a passkey need no proof
  of their own: they finish what a proven request started.
- The options of a passkey are good for one answer and five minutes. In a browser,
  `PublicKeyCredential.parseCreationOptionsFromJSON` takes them as they are and `credential.toJSON()` gives what
  `UserPasskeyRegisterRequest` carries. The origin of the client has to be in `Fido2:Origins`.
- Changes count against the logins per IP rate limit, as a proof can be guessed like a login.

## Several devices

A user can be logged in from several devices at once, each on its own connection. They are online while any of
them is connected; others hear `CoordinatorUserConnectedNotification` when the first one logs in and
`CoordinatorUserDisconnectedNotification` when the last one goes. What is sent to the user reaches all their
devices, and banning or deleting the user closes all of them.

- A connection that is logged in already is answered `AuthFailed` to another `UserAuthRequest`; it stays open and
  keeps its user.
- In a hybrid session the user takes part from one device. Joining it from another one takes the peer over: the
  device the user was on gets `HybridSessionPeerKickedNotification` with the reason "Joined from another device",
  and its media is gone. The others see the user rejoin.
- The media of the peer is driven only from the device it is on: connecting the transport, starting voice, video
  or a screen share, watching a screen, resuming inbounds, going AFK and leaving are refused from the other ones.
  Voice to listen to and pokes go to that device alone.
- When a device closes, the user leaves the hybrid sessions they took part in from it, and stays in the others.

## Not there yet

TOTP secrets are kept in the database as they are, like the server's other data; whoever can read the database
can make codes.
