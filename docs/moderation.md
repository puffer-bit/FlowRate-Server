# Global bans

Bans apply to an entire coordinator. Rooms have no ban operation or ban list; hosts may kick or reject a participant, but receive no global ban permission from hosting a room.

A moderator with `BanMembers` can ban a lower-ranked account or IP address, optionally supply a reason (up to 1000 characters), and choose a permanent ban or an expiry. Hardware is reserved in `BanKind` but is not accepted, collected or matched.

Account bans disconnect every device of that account. IP bans disconnect all connections from the normalized address, including unauthenticated ones, and block HTTP media/push requests and WebSocket upgrades. An IP ban does not ban the account when it connects from another address. IPv4-mapped IPv6 and IPv4 addresses compare equally. IP actions cannot bypass the moderator's own-account or role hierarchy protections through a shared address.

The ban dialog explicitly offers the account's last connection IP (leave the address empty) or a supplied address. Successful logins persist the last connection IP immediately; device sessions also retain their own last address.

`ModerationService` owns changes, revocation, account-state projection and disconnects. Admission and ban changes share a coordinator lock, so a connection cannot log in between saving a ban and disconnecting its target. Expired bans stop matching immediately. Account state is restored at admission, when listing bans, and by a 30-second sweep; a previously frozen account remains frozen. Account security proofs remain required even while banned, so expiry cannot bypass a password, passkey or two-factor authentication.

`GlobalBans` retains the target, reason, author, creation/expiry times and revocation author/time. Revocation is idempotent and preserves the reason. Deleted users do not delete this audit trail. The migration imports existing account/IP bans with an explicit legacy-reason placeholder, normalizes/deduplicates IP entries and clears the former settings list. New IP bans must use moderation, rather than the deprecated `BannedIps` settings field.

The client Users settings page and browser Bans page show active bans and history, and allow revocation. The browser can read history while a coordinator is stopped; changes require it running so every change uses the same moderation service. Client requests and REST use the same service. The panel acts as the system user (`-1`).

The disconnected user sees the reason from `CoordinatorUserBannedNotification`; a login refusal carries it in `ErrorResponse.Details`. Pre-upgrade IP refusals use HTTP 403 with UTF-8 reason encoded as base64 in `FlowRate-Ban-Reason`. The client stops automatic reconnects on a ban.

The protocol number is deliberately unchanged for this coordinated client/server update. The schema snapshot is updated. Old room-ban messages are removed; reasons remain optional for global-ban requests. Missing or whitespace-only reasons are stored as empty strings. Banned accounts remain visible in chat user lists with their ban state.
