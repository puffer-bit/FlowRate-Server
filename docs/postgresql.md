# PostgreSQL

SQLite remains the default. Without a `Database` section, the server uses
`server.db` under `Storage:Directory` and keeps its existing migration history.
`Storage:Directory` still holds media and certificates when PostgreSQL is selected.

To use an existing PostgreSQL database, set these environment variables before
starting the server:

```bash
export Database__Provider=PostgreSql
export Database__ConnectionString='Host=127.0.0.1;Port=5432;Database=flowrate;Username=flowrate;Password=replace-me'
```

The database user must be able to create and alter the application schema.
The server applies pending migrations before starting application services.
Keep credentials in deployment secrets or environment configuration rather than
committing them to appsettings.json. The connection string is required for PostgreSQL.
An explicit SQLite connection string is also supported; leaving it empty preserves
the default path under `Storage:Directory`.

SQLite and PostgreSQL have independent migration histories. PostgreSQL starts with
a new initial schema; selecting it does not copy data from an existing SQLite file.
Media files remain in local storage.

## Maintaining migrations

Run from `src/`. Runtime and EF tooling share the provider configuration.
Environment variables override `appsettings.json` in the current working directory;
arguments after `--` override both.

For PostgreSQL:

```bash
dotnet ef migrations add ChangeName \
  --project Server/Server.Migrations.PostgreSql \
  --startup-project Server/Server.Asp \
  --context ServerDbContext \
  -- --Database:Provider PostgreSql \
  --Database:ConnectionString 'Host=localhost;Database=flowrate;Username=flowrate'
```

For SQLite:

```bash
dotnet ef migrations add ChangeName \
  --project Server/Server.Infrastructure \
  --startup-project Server/Server.Asp \
  --context ServerDbContext --output-dir Database/Migrations \
  -- --Database:Provider Sqlite --Database:ConnectionString 'Data Source=data/server.db'
```

Every shared model change needs a migration for each provider. Keep existing SQLite
migrations intact. PostgreSQL uses native identity columns and UTC timestamps;
values written to `timestamp with time zone` must have `DateTime.Kind == Utc`.

The server trims saved PostgreSQL timestamps to microsecond precision so values
returned immediately after saving match subsequent reads. SQLite keeps its original
precision. Use the offline import command below to transfer an existing SQLite database.

## Docker Compose

Copy `.env.example` to `.env` and set `PUBLIC_IP` and `POSTGRES_PASSWORD`.
For literal dollar signs, single-quote the password in `.env`.

For the default host networking, put this in `.env`:

```dotenv
COMPOSE_FILE=docker-compose.yml:docker-compose.postgresql.yml
```

For bridge networking:

```dotenv
COMPOSE_FILE=docker-compose.yml:docker-compose.bridge.yml:docker-compose.postgresql.yml:docker-compose.postgresql.bridge.yml
```

Then run `docker compose up -d --build` from a source checkout, or
`docker compose up -d` with the release compose files. The server waits for PostgreSQL
and the SFU to become healthy. PostgreSQL 17 stores its data in `postgres-data`.
Host mode publishes PostgreSQL only on `127.0.0.1:${POSTGRES_PORT:-5432}`;
bridge mode has no published database port and connects to `postgres:5432`.
The password is passed through `PGPASSWORD`, without embedding it in a connection
string. Changing `POSTGRES_PASSWORD` affects initialization of a new volume only;
change an existing user's password in PostgreSQL before changing that setting.

Release archives and installer assets include both PostgreSQL overrides. The installer
preserves an existing PostgreSQL selection when updating or changing network modes.
The official PostgreSQL image is downloaded separately on first use.

### Backups

Create a database dump while using the same Compose selection as the running stack:

```bash
docker compose exec -T postgres pg_dump -U flowrate -d flowrate -Fc > flowrate-postgres.dump
```

Back up `server-data` too: media and certificates still live there. Coordinate database
and media backups while the server is stopped if they must describe the same state.
Keep `postgres-data` across container updates; `docker compose down` preserves it,
whereas adding `--volumes` deletes it. Switching providers creates an independent
database; run the explicit import below to transfer existing accounts, messages, and uploads.

## Integration tests

The default test run continues to use SQLite without Docker. Tests tagged
`Category=DatabaseProvider` also run against PostgreSQL in a separate CI job. They
cover message history and revisions, concurrent writes, pins, reactions, read states,
roles, sessions, passkeys, bans, uploads, private chats, settings, and migrations.
The same CI job checks SQLite imports, dry runs, sequence restoration, WAL reads,
and rollback on failures. Import integration tests are skipped in the default
SQLite run. Legacy SQLite migration tests remain in the default run.

Start an isolated PostgreSQL for a local test run, from the repository root:

```bash
PUBLIC_IP=127.0.0.1 POSTGRES_PASSWORD=integration_test POSTGRES_PORT=55439 \
  docker compose -p flowrate-postgres-tests -f docker-compose.yml \
  -f docker-compose.postgresql.yml up -d --wait postgres

FLOWRATE_TEST_DATABASE_PROVIDER=PostgreSql \
FLOWRATE_TEST_POSTGRES_CONNECTION_STRING='Host=127.0.0.1;Port=55439;Database=flowrate;Username=flowrate;Password=integration_test' \
  dotnet test src/Server/Server.Infrastructure.Tests --filter Category=DatabaseProvider
```

Each test creates a unique schema named `flowrate_test_<random>` and drops only that
schema afterwards. The test user needs permission to create schemas in the supplied
database. Selecting PostgreSQL without a connection string fails the test run.

Remove this isolated test stack and its test data afterwards:

```bash
PUBLIC_IP=127.0.0.1 POSTGRES_PASSWORD=integration_test POSTGRES_PORT=55439 \
  docker compose -p flowrate-postgres-tests -f docker-compose.yml \
  -f docker-compose.postgresql.yml down --volumes
```

## Importing an existing SQLite database

The published server includes an offline command; no separate tools installation is
needed. Stop the SQLite server and keep the PostgreSQL application server stopped
until the import finishes. Back up the entire existing storage directory first,
including SQLite WAL files if present, media, and certificates. Use the same server
version for the source database and the importer. An older or newer migration history
is rejected; upgrade a backup copy with the matching server before importing.

Configure the PostgreSQL target as described above, then run from the published
application directory:

```bash
dotnet Server.Asp.dll import-sqlite --source /path/to/storage/server.db --dry-run
dotnet Server.Asp.dll import-sqlite --source /path/to/storage/server.db
```

The target database must already exist. Its application tables must be empty; a
fresh schema is initialized automatically. `--dry-run` creates/applies the target
schema migrations, performs a full import and verifies it, then rolls back all rows
and sequence changes. Schema initialization remains after a dry run. A normal run
commits only after every table passes verification. Neither command merges into an
existing installation, clears target rows, upgrades SQLite, or starts server services.
Credentials are read from normal server configuration, including
`Database__Provider`, `Database__ConnectionString`, and `PGPASSWORD`.

### Using the Docker deployment

Stop the existing SQLite server before enabling PostgreSQL in `.env`:

```bash
docker compose stop server
```

After backing up storage, enable the PostgreSQL Compose selection above, preserving
the existing host/bridge networking choice. Keep the existing `server-data` volume
and Compose project name so the source database and media remain available. Build or
pull a server image that includes the import command. Start only PostgreSQL, then run
the one-off import process using the server image and its existing storage volume:

```bash
docker compose up -d --wait postgres
docker compose run --rm --no-deps server import-sqlite --source /data/server.db --dry-run
docker compose run --rm --no-deps server import-sqlite --source /data/server.db
docker compose up -d server
```

Do not start the normal server against a fresh PostgreSQL database before importing:
startup seeds configuration and roles, making that target nonempty. An import refuses
such a target; choose a fresh target rather than deleting the existing installation.

### What is transferred and verified

All mapped tables are streamed, including join tables, owned message content,
soft-deleted messages, account/session/passkey secrets, permissions, read states,
upload references, and node settings. IDs and foreign keys remain unchanged. SQLite
integrity, foreign keys, migration history, table names, and columns are checked before
initializing the target. Unknown source tables are rejected instead of silently omitted.
The importer locks the target tables, checks emptiness again, copies in dependency
order, and compares row counts and order-independent SHA-256 row fingerprints.
Target constraints remain enabled. Failed verification, conversion, or constraint
checks roll back all imported rows.

UTC date/time values are converted to PostgreSQL microsecond precision, including
nullable timestamps; intervals are also trimmed to microseconds. Other logical values,
including binary hashes, unsigned permission bits, and JSON-backed collections, use
the EF mappings for each provider. PostgreSQL sequences advance beyond both live IDs
and the SQLite AUTOINCREMENT high-water marks, including deleted IDs. Sequence
changes are transactional, so a dry run does not consume them.

The SQLite file is opened read-only and its migration history is retained. PostgreSQL
keeps its own independent migration history. Media and certificate files are not copied
by this command: retain `Storage:Directory`, or copy that directory separately when
moving machines. Paths and upload IDs stored in the database remain unchanged.

The command exits with 0 on success, 1 on an import/configuration failure, 2 for invalid
arguments, or 130 on Ctrl+C. Only a final `Import committed` message confirms completion;
per-table messages describe verification inside the transaction. If a connection fails
during commit, check the target before retrying. For rollback before accepting new
writes, stop the PostgreSQL server process and restore the previous SQLite configuration
and storage backup. Reverse migration of writes made after switching is not included.
