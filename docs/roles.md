# Roles and permissions

Each coordinator has its own roles, which work like Discord roles. A role is a named set of permissions with a
place in the coordinator's role order. A user can have any number of roles. What a user may do comes from all of
their roles together with `@everyone`.

## Concepts

### Roles

| Field         | Meaning                                                                               |
|---------------|---------------------------------------------------------------------------------------|
| `Name`        | 1–32 characters. It cannot be `@everyone`. Names do not have to be unique.            |
| `Color`       | `0xRRGGBB`, or none.                                                                  |
| `Position`    | Its place in the order. Higher ranks higher. `@everyone` is always at 0, the others at 1, 2, 3… with no gaps. |
| `Permissions` | Flags; see below.                                                                     |
| `IsEveryone`  | Marks the one role that every user has.                                               |
| `Revision`    | Goes up with every change of name, color or permissions. Used to detect conflicts.    |

A coordinator can have up to 100 roles (`Limits.RolesMaxCount`).

### @everyone

Every coordinator has exactly one `@everyone` role, and every user has it without it being assigned. It cannot
be renamed, given a color, moved, deleted, assigned or unassigned. Only its permissions can change.

### Permissions

`Shared.Enums.Permission` is a `[Flags] ulong`:

| Permission                  | Allows                                                                          |
|-----------------------------|---------------------------------------------------------------------------------|
| `Administrator`             | Everything else. The role order still applies.                                  |
| `ViewCoordinatorSettings`   | Reading the coordinator settings (`CoordinatorSettingsRequest`).                |
| `ManageCoordinatorSettings` | Changing them (`CoordinatorUpdateSettingsRequest`).                             |
| `ManageRoles`               | Creating, changing, reordering, deleting, assigning and unassigning roles below one's own highest role. |
| `CreateSessions`            | Creating sessions. The creator of a hybrid session becomes its host.            |
| `ManageSessions`            | Changing and deleting any session, and arranging sessions and categories.       |
| `KickMembers`               | Kicking from any hybrid session.                                                |
| `BanMembers`                | Banning from any hybrid session.                                                |
| `ApproveMembers`            | Approving or rejecting users who wait to enter a hybrid session.                |
| `ManageMessages`            | Deleting other users' chat messages.                                            |
| `ManageUsers`               | Changing other users' names and avatars.                                        |

A user's permissions are the bitwise OR of their roles and `@everyone`. If the result contains `Administrator`,
it becomes `Permission.All`.

### Role order

A role manages only what is below it:

- **Managing a role** (change, move, delete, assign, unassign) requires the role to be strictly below the
  actor's highest role.
- **Granting permissions:** an actor cannot add a permission they do not have themselves. A role may keep
  permissions it already has when someone without them edits it.
- **Acting on a user** (kick, ban, rename, assign or unassign roles) requires the target's highest role to be
  strictly below the actor's. This applies to administrators too, so two users with the same highest role
  cannot act on each other. Acting on yourself is always allowed.
- **Reordering:** roles at or above the actor's highest role must keep their positions. A moved role must stay
  below the actor.

### Session hosts

The host of a hybrid session (`HybridSessionModel.HostId`) has `RequestAuthorizer.HostPermissions` in that
session, whatever their roles: `ManageSessions`, `KickMembers`, `BanMembers` and `ApproveMembers`. A host may
act on users of the same rank, so a plain user can moderate their own session. Users ranked above the host are
out of reach. A request goes through if either the role or the host route allows it.

### The system user and the panel

`UserModel.SystemUserId` (-1) bypasses every check. The control panel acts as the system user, and that is how
the first administrator is made: a new coordinator has an `Admin` role (`Administrator`) that nobody has, and
the panel assigns it.

## Defaults

`RoleService` loads a coordinator's roles the first time they are needed: when the coordinator starts, or on the
first panel or role call. If the coordinator has no `@everyone` role yet, two roles are created:

| Role        | Position | Permissions      |
|-------------|----------|------------------|
| `@everyone` | 0        | `CreateSessions` |
| `Admin`     | 1        | `Administrator`  |

Everything else needs a role. This includes viewing the coordinator settings and managing sessions that are not
your own.

## Client protocol (Shared)

All role requests are `CoordinatorRequest`s.

| Request                          | Needs         | Response                                                   |
|----------------------------------|---------------|------------------------------------------------------------|
| `CoordinatorRolesRequest()`      | —             | `CoordinatorRolesResponse(Result, Roles, Permissions)`: the roles, lowest first, and the sender's permissions |
| `CoordinatorCreateRoleRequest(Role)` | `ManageRoles` | `CoordinatorRoleResponse(Result, Role)`                |
| `CoordinatorUpdateRoleRequest(RoleId, Role, ExpectedRevision)` | `ManageRoles` | `CoordinatorRoleResponse`; on `Conflict`, `Role` is the current role |
| `CoordinatorDeleteRoleRequest(RoleId)` | `ManageRoles` | `CoordinatorRoleResponse(Result, null)`                |
| `CoordinatorReorderRolesRequest(RoleIds)` | `ManageRoles` | `CoordinatorRolesResponse(Result, Roles, None)`. `RoleIds` lists every role except `@everyone`, lowest first; a list that does not match the current set is a `Conflict`. |
| `CoordinatorAssignRoleRequest(UserTargetId, RoleId)` | `ManageRoles` | `CoordinatorUserRolesResponse(Result, UserTargetId, RoleIds)` |
| `CoordinatorUnassignRoleRequest(UserTargetId, RoleId)` | `ManageRoles` | `CoordinatorUserRolesResponse`                |

`RoleUpdateDTO` holds `Name`, `Color` and `Permissions`. `RoleDTO` adds `Id`, `Position`, `IsEveryone` and
`Revision`. `UserDTO.RoleIds` lists a user's roles without `@everyone`.

`CoordinatorRoleResult` values: `NoError`, `InsufficientPermissions`, `HierarchyViolation`, `Conflict`,
`RoleNotFound`, `UserNotFound`, `InvalidRole`, `TooManyRoles`, `InternalError`. `TimedOut` and `Rejected` are
for the client's own use.

### Push notifications

| Notification                                           | Sent to                    | When                                   |
|--------------------------------------------------------|----------------------------|----------------------------------------|
| `CoordinatorRolesChangedNotification(Roles)`            | everyone                   | a role was created, changed, moved or deleted |
| `CoordinatorUserUpdatedNotification(User)`              | everyone                   | a connected user's roles changed (`User.RoleIds`) |
| `CoordinatorPermissionsChangedNotification(Permissions)` | each affected user, alone | their permissions may have changed     |

Changes reach clients no matter where they were made: the panel or another client.

### Refused requests

Requests that the sender's roles do not allow are stopped in `MessageRouter` before any handler runs. The sender
gets `RequestRejectedResponse(RequestId, RequestRejectReason.InsufficientPermissions)` instead of the normal
response. Unlike invalid requests, these refusals do not count toward closing the connection.

Two checks need data that only the handler has, so they are made later:

- **Deleting another user's chat message:** `DeleteTextMessageResponse` with `InsufficientPermissions`.
- **Role order inside role changes:** `CoordinatorRoleResult.HierarchyViolation`.

## Control panel API

Everything is under `api/panel/coordinators/{id}` and runs as the system user.

| Method   | Path                                 | Body              | Answer                                             |
|----------|--------------------------------------|-------------------|----------------------------------------------------|
| `GET`    | `roles`                              | —                 | `RoleDto[]`, lowest first                          |
| `POST`   | `roles`                              | `RoleChangeDto`   | `RoleDto`; 400 if invalid or there are too many roles |
| `PUT`    | `roles/{roleId}`                     | `UpdateRoleDto`   | `RoleDto`; 409 if the revision is stale            |
| `DELETE` | `roles/{roleId}`                     | —                 | 204                                                |
| `PUT`    | `roles/order`                        | `ReorderRolesDto` | `RoleDto[]`; 409 if the list is stale              |
| `GET`    | `users`                              | —                 | `CoordinatorUserDto[]`: every user of the coordinator with `roleIds` |
| `PUT`    | `users/{userId}/roles/{roleId}`      | —                 | the user's role ids                                |
| `DELETE` | `users/{userId}/roles/{roleId}`      | —                 | the user's role ids                                |

On the panel API, permissions are a list of single flag names, for example
`["KickMembers", "BanMembers"]`, not a combined number.

Every saved change, whoever made it, sends the SSE event `coordinatorRolesChanged` with the coordinator id. The
panel then refetches the roles and the users. The page is at `/panel/coordinators/{id}/roles`.

## Server layout

| Where | What |
|-------|------|
| `Server.Core/Models/Roles/RoleModel.cs` | One role and its own rules (name, color, permissions, position, the everyone role). |
| `Server.Core/Models/Roles/RoleSet.cs` | All roles of a coordinator: permissions of a set of roles, `Outranks`, `CanManage`, `CanGrant`. |
| `Server.Application/Orchestrator/Roles/RoleService.cs` | The single entry point for reading and changing roles, used by both the panel and the coordinator handlers. It loads and caches the roles per coordinator, creates the defaults, checks permissions and role order, saves, and raises `RolesChanged`. Changes are serialized. |
| `Server.Application/Coordinator/Roles/CoordinatorRoles.cs` | Per coordinator: loads the roles at start, answers `ICoordinatorPermissions`, and passes `RolesChanged` on to connected users. |
| `Server.Application/Coordinator/Roles/RequestPolicies.cs` | What each request needs. |
| `Server.Application/Coordinator/Roles/RequestAuthorizer.cs` | Applies a policy: the role route, the host route, and the role order against the target user. |
| `Server.Application/Coordinator/Handlers/Roles/` | Handlers for the role requests. |
| `Server.Infrastructure/Database/Repositories/RoleRepository.cs` | The `Roles` and `UserRoles` tables. `Permission` is stored as a signed 64-bit integer. |
| `Server.Asp/Web/Api/RolesController.cs`, `Web/Realtime/RolePanelEvents.cs` | Panel API and live updates. |

### Adding a request

Every request type registered in `BaseMessage` must have an entry in `RequestPolicies.Of`, even if it is open
(`RequestPolicy.Open`). A request with no entry is refused, and `RequestAuthorizerTests.EveryRequest_HasAPolicy`
fails. The policy can be:

- `Require(permission)`: the sender needs the permission.
- `InSession(sessionId, permission)`: the host of that hybrid session may make the request too.
- `.Against(userId)`: additionally, the target user must rank below the sender (or, through the host route, at
  most at the sender's rank).

### Adding a permission

1. Add the flag to `Shared.Enums.Permission` and include it in `All`.
2. Use it in `RequestPolicies`, or check `ICoordinatorPermissions.Has` where the decision needs data.
3. Add it to the `Permission` union in the panel's `schema.d.ts` (`npm run gen:api` regenerates the file) and to
   the `permissions` list in `RolesPage.tsx`.

## Client (FlowRateClient)

- `ICoordinatorSession` loads the roles at every login (`CoordinatorRolesRequest`) and keeps
  `Permissions` (`Client.Core.Coordinator.CoordinatorPermissions`). This object mirrors the server's rules:
  `Has`, `CanManage`, `CanAssign`, `CanGrant`, `CanInSession` and `CanInSessionOn` (the host rule).
  `PermissionsChanged` fires when a role changes or when the current user's roles change.
  `CoordinatorPermissionsChangedNotification` is ignored, because every client already has the roles and can
  compute its own permissions.
- `AccessVM` (owned by `MainWindowVM`) passes the permissions to the views. Actions the user's roles do not
  allow are hidden:
  - creating sessions and categories;
  - arranging the list (drag and drop, moving, renaming and deleting categories);
  - session settings and deletion (allowed to the host or to `ManageSessions`);
  - kicking from the sidebar and from the participant list;
  - deleting other users' messages;
  - editing other users' profiles;
  - the server settings window.
- The server settings window shows the settings tabs only with `ViewCoordinatorSettings`. Without
  `ManageCoordinatorSettings` they are read-only. The window has a **Roles** tab for `ManageRoles`, with a
  list, ordering, an editor and deletion. Every role change is saved at once, and changes made elsewhere show
  up live.
- The user profile lists the user's roles. Roles the viewer may assign appear as check boxes on both desktop
  and Android.
- `RequestRejectReason.InsufficientPermissions` is shown as its own message.

## Not done yet

- Permission overrides per session, like Discord channel overwrites. A future `SessionPermissionOverwrite` (allow and
  deny per role or user) would plug into `RequestAuthorizer` next to the host route.
- Hiding IP addresses from users who lack a permission. `UserDTO.Ip` still reaches every client.
- Panel authentication. Anyone who can open the panel can make themselves an administrator. This must be solved
  before a release.
- Role colors are not shown next to user names anywhere except the profile and the roles tab.
