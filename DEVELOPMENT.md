# Server development

Requirements: .NET SDK 10, Node.js 24 or newer, Docker for the full stack.

```sh
src/Scripts/bash/build.sh     # the solution
src/Scripts/bash/test.sh      # every test project
src/Scripts/bash/run.sh       # the server; --panel builds the control panel first
cd src/Sfu/mediasoup && npm ci && npm run dev   # the SFU, configured by .env (see .env.example)
```

PowerShell versions of the scripts live in `src/Scripts/powershell`. The control panel has its own
[README](src/Web/admin-panel/README.md).

Layout:

- `src/Server` — the server, layered as Core → Application → Infrastructure → Asp, with a test project for each;
  `Server.PushRelay` is an optional relay that forwards pushes to FCM for servers without a Firebase key.
- `src/Sfu/mediasoup` — the SFU.
- `src/Web/admin-panel` — the control panel (React, TypeScript, Vite).
- `src/Shared` — the protocol shared with the client, a git submodule
  ([FlowRate-Shared](https://github.com/puffer-bit/FlowRate-Shared)).
- `docs` — [authentication](docs/authentication.md) and [roles](docs/roles.md).

