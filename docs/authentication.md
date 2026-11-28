# Logging in: devices, recovery phrase, factors

An account is a set of **devices**. Each device has its own P-256 key: the client keeps the private part (in the
system keyring, the Android Keystore or the TPM), the server only the public one. A device logs in by signing a
challenge, so nothing a client sends to log in is a secret the server could leak or replay, and a copy of the server
database logs nobody in.

| What | Role | Kept by the client | Kept by the server |
|------|------|--------------------|--------------------|
| Username | Finds the account for a login by username | — | the name |
| Device key | Logs the device in by signing a challenge | private key, never exportable where the platform allows | public key (`UserSessions.PublicKey`) |
| Recovery phrase | Adds a device on its own; later the root of end-to-end encryption keys | the user (paper, password manager); the keyring until the user confirms saving it | SHA-256 of the public key it derives (`UserEntities.RecoveryKeyHash`) and the account identity key (`AccountPublicKey`) |
| Password, one-time codes, passkeys | Factors of a login by username | — | PBKDF2 hash, TOTP secret, passkey public keys |
| Server password | Asked of every login while the coordinator has one | the keyring, when remembered | PBKDF2 hash |
| Login code | Adds a device once, for a user who lost everything | — | SHA-256, 15 minutes |

The protocol is in `Shared/Docs/Protocol.md` (*Logging in and devices*); this page is the server side.

## Keys and signatures

- Keys are P-256. Public keys travel as SubjectPublicKeyInfo DER, signatures as r‖s (IEEE P1363, 64 bytes), both
  base64url. `Shared.Auth.LoginSignature` holds what is signed and how it is checked, for both sides.
- The payload is `FlowRate login v1\n{purpose}\n{coordinatorId}\n{nonce}\n{newDevicePublicKey}`, purpose `device` or
  `recovery`. The nonce comes from `LoginChallengeRequest`: 32 random bytes, one per connection, good for one login and
  two minutes (`LoginNonces`). A recovery login signs the key of the device it adds too, so the signature cannot be
  reused for another device.
- The recovery phrase is 24 BIP-39 words (256 bits). The client derives three keys with HKDF-SHA256 (salt
  `FlowRate recovery v1`): `login` (the recovery key, P-256), `identity` (the account key for end-to-end encryption,
  P-256) and `backup` (256 bits for encrypted backups). The server never sees the phrase.

## The login exchange

```
client                                                       server
  │  LoginChallengeRequest ───────────────────────────────────► │
  │ ◄───────────────────── LoginChallengeResponse(Nonce, Id)    │
  │  LoginRequest(ServerPassword, Method, Device) ────────────► │  server password, then the method
  │ ◄──────────── LoginResponse(Success, User, DeviceId)        │
  │                    or LoginResponse(SecondStageRequired,    │  username / legacy identity with factors
  │                       Factors, PasskeyOptions)              │
  │  LoginProofRequest(Password, TwoFactorCode) ──────────────► │  either one
  │  LoginPasskeyRequest(Assertion) ──────────────────────────► │
  │ ◄──────────── LoginResponse(Success, User, DeviceId)        │
```

`LoginHandler` takes the methods:

| Method | Checks | Adds a device |
|--------|--------|---------------|
| `Device` | The device exists and has not expired (`DeviceNotFound` otherwise), and signs the nonce | no, it lives on: `ExpiresAt` moves forward |
| `Register` | The keys are P-256 and not taken | yes, `Registration`; the user gets a generated name |
| `Recovery` | A user has the hash of the recovery key, and it signs the nonce and the new device key | yes, `Recovery` |
| `Username` | The name finds exactly one user, ignoring case; the user has a factor (`LoginFactorsMissing` otherwise) | yes, `Factors`, after the second stage |
| `LoginCode` | A code the panel gave out, not expired; it is deleted as it is used | yes, `LoginCode` |
| `Legacy` | The identity of the earlier protocol 3; a live session token of the same user skips the second stage | yes, `Legacy`, see below |

- Every login but a resumed device goes through `UserLogin.AddDeviceAsync`: bans are checked
  (`ModerationService.AdmitAsync`), the user is attached (online and connection limits), and only then the device is
  saved. The user's other connections get `AccountDeviceAddedNotification`.
- A wrong server password, signature, code or name closes the connection after the `ErrorResponse`. The second stage
  allows three tries in two minutes; a one-time code works for one login.
- Login requests count against `RateLimits.AuthenticationPerIp`, and so do the account security changes and the
  device link requests, so codes cannot be guessed.

### Moving accounts over from the earlier protocol 3

Users of the earlier pre-releases kept an identity (`#` and a GUID) and session tokens. The client sends them once in
`LegacyLoginDTO`, with the recovery keys of a phrase it made. `LegacyAccounts.MoveOverAsync` then, once the user is let
in: sets the recovery keys, which drops `IdentityHash`; deletes the sessions without a key; adds the device. The
identity does not work again. A user who had factors and no live token passes the second stage first.

## Devices

`UserSessions` (the table keeps its name, `UserSessions`) holds the devices:

- A device ends after 90 days without a login, a temporary one (`NewDeviceDTO.IsTemporary`) after one day. Expired
  ones are deleted at the next device added.
- `AccountDevicesRequest`, `AccountDeviceRevokeRequest` (one device but the current one, or all others) and
  `LogoutRequest` (the current one). A removed device that is connected gets `AccountDeviceEndedNotification` with
  `Revoked` or `LoggedOut` and is closed.
- The panel lists and removes devices in **Roles → Users → Login**, through `UserDevicesController`.

### Adding a device from another one

`DeviceLinkStartRequest` (logged out, with the server password) gives the new device an eight-character code
(`DeviceLinks`, no 0/O/1/I/L), good for five minutes. Its connection waits: `IClientConnection.WaitsForDeviceLinkUntil`
keeps the authentication timeout from closing it until then. A device of the account looks the code up with
`DeviceLinkInfoRequest` and answers `DeviceLinkApproveRequest`; approval runs the same `AddDeviceAsync` on the waiting
connection, which gets `DeviceLinkCompletedNotification` and is logged in. A declined or failed link sends
`DeviceLinkDeclinedNotification` and closes it.

## Login codes

The panel gives out a login code for a user in **Roles → Users → Login → Give out a login code**
(`POST /api/coordinators/{id}/users/{userId}/login-code`): twelve characters in three groups, shown once, good for one
device and 15 minutes. Codes given before and still unused are dropped. Only the SHA-256 of the code is kept
(`UserLoginCodes`); case and dashes do not matter.

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
| `UserRecoveryKeyResetRequest` | yes   | Replaces the recovery keys with those of a new phrase; the devices stay |

- The proof (`UserSecurityProofDTO`) is the current password and one-time code, each when the user has it; a
  first password needs none. A missing or wrong one is answered `ProofRequired`. So an open session alone cannot
  change how the account logs in, nor lock its owner out.
- The options of a passkey are good for one answer and five minutes.

## Passkeys

`FIDO2LoginOptionsDTO` holds what `navigator.credentials.get` (or a platform WebAuthn API) needs: `Challenge`, `RpId`,
`Timeout`, `AllowedCredentials[].Id` and `UserVerification` (always `required`). The parts of `FIDO2AssertionDTO` are
base64url (plain base64 is accepted too). The server checks the challenge, the origin, the relying party, the
signature, user verification and that the signature counter grows.

Passkeys are off until the relying party is set (`Fido2:RelyingPartyId`, `PASSKEY_DOMAIN` with Docker). Changing it
makes every registered passkey useless. The client apps do not log in with passkeys yet; the panel registers them.

## Several devices

A user can be connected from up to five devices at once, each on its own connection; the sixth is refused with
`RateLimited`. `MaxOnlineUsers` counts distinct users. In a hybrid session the user takes part from one device; joining
from another takes the peer over.

## Not there yet

- TOTP secrets are kept in the database as they are, like the server's other data; whoever can read the database can
  make codes.
- The account identity key is kept, not used: end-to-end encryption will sign the keys of devices with it.
