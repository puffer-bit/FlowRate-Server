# FlowRate admin panel

Browser control panel for the server. React + TypeScript + Mantine, built with Vite.
Server.Asp serves the production build under `/panel/`; the data comes from `/api/panel/*`.

## Development

Start the server (Development environment, so `/openapi/v1.json` is available), then:

```sh
npm install
npm run dev        # http://localhost:5173/panel/, proxies /api to https://localhost:7114
```

Point the proxy elsewhere with `PANEL_API=https://host:port npm run dev`.

## API types

`src/api/schema.d.ts` is generated from the server's OpenAPI document. After changing the API contracts in
`Server.Asp/Web/Api/Contracts`, with the server running:

```sh
npm run gen:api    # or: npm run gen:api -- https://host:port
```

## Build

```sh
npm run build      # writes to ../../Server/Server.Asp/Web/wwwroot/panel (git-ignored)
```

## Layout

- `src/api/client.ts` — REST calls and DTO types, `isNotFound` / `isConflict` for error answers.
- `src/api/realtime.ts` — the live updates transport (server-sent events). The only module that knows about it.
- `src/api/hooks.ts` — queries, and `useLiveUpdates`, which refetches what the server says changed
  (`staleQueries` maps each signal to the queries it makes stale).
- `src/api/webauthn.ts` — the browser side of adding a passkey.
- `src/pages/` — separate statistics, coordinator list, SFU node list, coordinator details, settings and roles pages. Roles are described in
  `docs/roles.md` at the repository root.
- `src/components/UserSecurityModal.tsx` — password, one-time codes and passkeys of a user, opened from the users
  of the roles page; see `docs/authentication.md`. Passkeys need the panel opened by the `Fido2:RelyingPartyId`
  name (`localhost` in Development).

## Server side

Everything the panel talks to lives in `Server.Asp/Web`:

- `PanelRoutes.cs` — the paths of the API, the change stream and the app; keep `client.ts` and `realtime.ts` in
  line with them.
- `PanelExtensions.cs` — `AddPanel` / `MapPanel`, the only panel lines in `Program.cs`.
- `Api/` — controllers and their contracts (the source of `schema.d.ts`).
- `Realtime/` — what turns server changes into change signals (`*PanelEvents`) and the SSE transport.

Every change the panel shows is signalled: sessions and users of a coordinator, coordinators starting, failing
and stopping, settings, roles and SFU node connections. Only SFU round trips are polled.

## Creating coordinators

Open **Coordinators → Create coordinator**. Choose a name, local listen IP, free port, user limits and the SFU
nodes available to the coordinator. Coordinators run in the server process and can share SFU nodes. Each needs
its own non-overlapping listen endpoint; expose its TCP port when clients connect from another machine.

The server validates the endpoint and SFU IDs before saving and serializes creation requests to enforce the
stored `MaxCoordinators` limit (4 by default). New coordinators use a 5-second heartbeat interval, a 40-second
timeout and the standard media, upload and rate limit settings. Without assigned SFU nodes, voice and video
sessions cannot start. The configuration survives server restarts.

`POST /api/panel/coordinators` returns `201` with the saved ID and actual state. Validation errors return `400`
with field errors, the coordinator limit returns `409`, and server startup/shutdown returns `503`. If a listener
fails after the preflight check, the saved coordinator remains visible as **Failed**. Open its settings, correct
the configuration and save to retry; other coordinators continue running.
