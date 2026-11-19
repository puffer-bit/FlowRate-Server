# Releasing the server

The release version comes from `InformationalVersion` in
`src/Server/Server.Asp/Server.Asp.csproj`. Keep `src/Shared` at the tested commit;
the client must support the server's protocol range (currently 4–5).

## Validate the candidate

From a clean checkout with submodules initialized:

```sh
cd src
dotnet test FlowRateServer.sln -c Release
cd Sfu/mediasoup
npm ci
npm test
cd ../../Web/admin-panel
npm ci
npm run build
```

`Release` runs these checks before building or publishing images. The panel check
includes Vite's production build; the SFU check includes its authentication and
media profile tests and starts a native mediasoup worker for the gRPC checks.

Build the local Docker package from the repository root:

```sh
src/Scripts/bash/publish.sh
```

This creates the archive in `dist/` and the online installer assets in
`dist/install/`. It does not publish them. Test the archive on a disposable Linux
machine with free ports, using its public IP. Check both a fresh install and an
update of the previous release with existing users and rooms. Verify:

- Both containers become healthy and the server connects to the SFU.
- The panel opens through the SSH tunnel and its static assets load.
- A compatible client signs in, sends chat messages, joins voice and shares a screen.
- SFU credentials work; incorrect credentials prevent connection; correcting them restores media.
- Restarting the stack preserves users, rooms and files.

## Publish

Push the reviewed commit to the intended release branch and run **Release** in
GitHub Actions for that branch. Keep `prerelease` enabled for the candidate.
The workflow validates the commit, builds native x64 and arm64 images, pushes
them to GHCR, and publishes the installer and offline archives on that commit.
Ensure both GHCR packages are public so new installations can pull them.

After testing the candidate, run **Release** on the same commit with `prerelease`
disabled to publish `v<InformationalVersion>` and update the `latest` image tags.
An existing stable version requires a version bump before another release.

## Update and rollback

Before updating a deployed server, stop the stack and back up `.env`, `secrets/`
and the `flowrate_server-data` volume, then restart it or install the update.
Keep the previous release tag and its archive available. Run the new installer;
it keeps the configuration and volumes. Verify sign-in, chat and media after
the update.

To roll back, stop the stack, restore that backup into the same installation,
and run the previous release's installer. Restore the database together with
the old image: database changes may prevent an older server from reading an
upgraded database. Do not use `docker compose down -v` on a live installation.
