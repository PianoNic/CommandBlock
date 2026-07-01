# Self-host CommandBlock

Run CommandBlock from the pre-built image, on either registry:

- **GitHub Container Registry** - `ghcr.io/pianonic/commandblock:latest`
- **Docker Hub** - `pianonic/commandblock:latest`

The two are identical; use whichever you prefer. Examples below use the GHCR tag.

You need a Linux/Windows host with **Docker + Compose v2**, and a directory to keep state in.

## Pick your path

| You have… | Do this |
| --- | --- |
| Your own OIDC provider (Pocket ID, Authentik, Auth0, Keycloak…) | [Quickstart](#quickstart) below - two files, no clone. |
| Nothing yet, want zero-config auth | [Bundled Keycloak](#no-oidc-provider-bundled-keycloak) - clone the repo. |
| A single machine, just you | The [desktop app](./desktop.md) - SQLite, built-in login, no Docker auth setup. |

## Quickstart

Drop these three files in an empty folder and run `docker compose up -d`. Open <http://localhost:5000>. State lives in `./db/`, `./backups/`, and `/data/commandblock/` (provisioned instance data).

**`compose.yml`**

```yaml
services:
  commandblock:
    image: ghcr.io/pianonic/commandblock:latest   # or pianonic/commandblock:latest (Docker Hub)
    container_name: commandblock
    restart: unless-stopped
    extra_hosts:
      - "host.docker.internal:host-gateway"   # how commandblock reaches provisioned DBs
    depends_on:
      db:
        condition: service_healthy
    ports:
      - "5000:8080"
    environment:
      Database__Provider: "Postgres"
      ConnectionStrings__CommandBlockDatabase: "Host=db;Port=5432;Database=commandblock;Username=postgres;Password=${POSTGRES_PASSWORD}"
      Vault__MasterKey: ${CommandBlock_VAULT_KEY}
      CommandBlock__PublicUrl: ${CommandBlock_PUBLIC_URL}              # login redirect + CORS are derived from this
      Cors__AllowedOrigins__0: ${CommandBlock_PUBLIC_URL}
      Oidc__Authority: ${CommandBlock_OIDC_AUTHORITY}
      Oidc__ClientId: ${CommandBlock_OIDC_CLIENT_ID}
      Oidc__Scope: "openid profile email roles"
      Oidc__RequireHttpsMetadata: "true"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock   # Windows: //var/run/docker.sock
      - ./backups:/app/backups
      - ./commandblock.yaml:/app/commandblock.yaml:ro             # port ranges, storage, nodes, declarative instances
      - /data/commandblock:/data/commandblock                     # provisioned instance data (HostFolder storage)

  db:
    image: postgres:18.4
    container_name: commandblock-db
    restart: unless-stopped
    environment:
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: commandblock
    volumes:
      - ./db:/var/lib/postgresql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d commandblock"]
      interval: 2s
      timeout: 3s
      retries: 30
```

**`.env`**

```env
POSTGRES_PASSWORD=change-me
CommandBlock_VAULT_KEY=GENERATE_ME          # openssl rand -base64 32

# The public URL CommandBlock is served on. The login redirect URI and CORS origin are derived from it.
CommandBlock_PUBLIC_URL=http://localhost:5000

# Your OIDC provider:
CommandBlock_OIDC_AUTHORITY=https://auth.example.com/realms/commandblock
CommandBlock_OIDC_CLIENT_ID=commandblock
```

**`commandblock.yaml`** - the host-port ranges CommandBlock allocates from per engine. Copy as-is:

```yaml
commandblock:
  storage:
    mode: HostFolder    # provisioned data lives in /data/commandblock on the host (set mode: Volume for named docker volumes)
    host_path: /data/commandblock
  port_ranges:
    postgres:     30000-30099
    timescaledb:  30100-30199
    mysql:        30200-30399
    mariadb:      30400-30599
    mssql:        30600-30799
    mongo:        30800-30999
    redis:        31000-31199
    cockroachdb:  31200-31399
    clickhouse:   31400-31599
    cassandra:    31600-31799
    couchdb:      32000-32199
    neo4j:        33200-33399
    qdrant:       34000-34199
    valkey:       34200-34399
    seaweedfs:    34400-34599
    pgvector:     34600-34799
    azurite:      34800-34999
```

::: warning
With `HostFolder` storage, `/data/commandblock` must be writable by the engine containers, which run as fixed UIDs. If a provision fails right after the container starts, `chown` the folder (e.g. `sudo chown -R 999:999 /data/commandblock` for Postgres) - or switch to `mode: Volume` to let Docker manage it.
:::

On your IdP, register CommandBlock as a **public client** (PKCE, no secret) with redirect URI `http://localhost:5000/*`. That's it - the rest is reference below.

## No OIDC provider? Bundled Keycloak

For zero-config auth, clone the repo. Its `compose.yml` ships Keycloak with a ready-to-import realm:

```bash
git clone https://github.com/PianoNic/CommandBlock.git && cd CommandBlock
cp .env.example .env     # edit before first start
docker compose up -d     # postgres + keycloak + commandblock
```

First boot imports the `commandblock` realm (~30-60s). Then open Keycloak at <http://localhost:8080>, log in as the bootstrap admin from `.env`, switch to the **commandblock** realm, and add a user under **Users → Add user**. Log in to CommandBlock at <http://localhost:5000>.

---

## Configuration reference

<details>
<summary><strong>Environment variables</strong></summary>

Set these on the `commandblock` service (the Quickstart pulls them from `.env`).

| Variable | What it does |
| --- | --- |
| `Vault__MasterKey` | AES-256 key for the secrets vault. **32 random bytes, base64** (`openssl rand -base64 32`). Encrypts every instance password. |
| `ConnectionStrings__CommandBlockDatabase` | CommandBlock's own metadata DB. Postgres: `Host=db;Port=5432;Database=commandblock;Username=postgres;Password=…`. SQLite: `Data Source=/data/commandblock.db`. |
| `Database__Provider` | `Postgres` or `Sqlite` (default). Picks the metadata store. |
| `Oidc__Authority` | Public IdP discovery URL. Must match the `issuer` in `<authority>/.well-known/openid-configuration` **byte-for-byte** (scheme, port, trailing slash). |
| `Oidc__ClientId` | Client ID registered on the IdP (public/PKCE). |
| `Oidc__RedirectUri` / `…PostLogoutRedirectUri` | Return URL after login/logout. Must be registered on the IdP, keep the trailing slash. |
| `Oidc__Scope` | `openid profile email roles` (`roles` optional). |
| `Oidc__RequireHttpsMetadata` | `true` (set `false` only for a plain-HTTP IdP). |
| `Cors__AllowedOrigins__0` | Browser origin allowed to call the API - CommandBlock URL **without** trailing slash. Add more as `__1`, `__2`. |
| `CommandBlock__PublicUrl` | Public URL this control plane is served on (e.g. `https://commandblock.example.com`). Used to pre-fill the [Add-node](./nodes#add-a-node) compose. Optional. |
| `Backup__Directory` | Where dumps are written. Optional - defaults to `/app/backups` (the path the compose bind-mounts). |
| `Docker__Endpoint` | Docker daemon URI, e.g. `unix:///var/run/docker.sock`. Optional - auto-detected (Unix socket / Windows named pipe) when unset. |

::: danger
**Never lose or rotate `Vault__MasterKey` once you have data.** There's no recovery and no key-rotation flow.
:::

</details>

<details>
<summary><strong>SQLite instead of Postgres</strong></summary>

Drop the `db` service and point CommandBlock at a file on a mounted volume:

```yaml
    environment:
      Database__Provider: "Sqlite"
      ConnectionStrings__CommandBlockDatabase: "Data Source=/data/commandblock.db"
    volumes:
      - ./db:/data
```

Remove `depends_on: db`. The file **must** be on a volume or it's wiped on every recreate.

</details>

<details>
<summary><strong>Instance data on a host folder</strong></summary>

By default each provisioned instance gets a named Docker volume. To see the raw files instead, set in `commandblock.yaml`:

```yaml
commandblock:
  storage:
    mode: HostFolder
    host_path: /data/commandblock     # path on the Docker HOST
```

New provisions then bind-mount `${host_path}/${containerName}`. Two gotchas: engines run as a fixed UID and need write access (`chown 999:999 …` for Postgres), and delete-cleanup only works if `host_path` is mounted into the commandblock container at the same path.

</details>

<details>
<summary><strong>OIDC provider setup &amp; quirks</strong></summary>

CommandBlock's SPA uses Authorization Code Flow + **PKCE**, so register a **public client** (no secret). Configure on the IdP:

| Setting | Value |
| --- | --- |
| Client type | Public (PKCE) |
| Redirect / post-logout URI | `http://localhost:5000/*` (or your public URL) |
| Web origins / CORS | CommandBlock origin, no trailing slash |
| Scopes | `openid profile email` (`roles` optional) |

- **Pocket ID** - toggle **Public Client**; allow your user/group. `*` wildcards work.
- **Authentik** - use the *Provider*'s issuer (`/application/o/<slug>/`) as the authority, not the Application URL.
- **Auth0** - authority is `https://<tenant>.auth0.com/` (trailing slash); app type **SPA**.
- **External Keycloak** - authority is `https://<host>/realms/<realm>`.

</details>

<details>
<summary><strong>Reverse proxy (Caddy/Traefik/nginx)</strong></summary>

Make the public origin match `Oidc__RedirectUri` and `Cors__AllowedOrigins__0`, trust `X-Forwarded-*`, and serve over HTTPS.

```caddy
commandblock.example.com { reverse_proxy commandblock:8080 }
```

```env
Oidc__RedirectUri=https://commandblock.example.com/
Oidc__PostLogoutRedirectUri=https://commandblock.example.com/
Oidc__RequireHttpsMetadata=true
Cors__AllowedOrigins__0=https://commandblock.example.com
```

With the bundled Keycloak, also set `KC_HOSTNAME=https://sso.example.com` and the matching `Oidc__Authority`. `Oidc__InternalAuthority` stays the in-cluster URL.

</details>

---

## Operations

**Upgrade**

```bash
docker compose pull commandblock && docker compose up -d commandblock
```

Migrations run on startup; the vault, metadata, and provisioned containers are preserved. Pin a version by replacing `:latest` with a [published tag](https://github.com/PianoNic/CommandBlock/pkgs/container/commandblock).

**Back up** the `./db/` (or `postgres-data` volume) and `./backups/` directories before major upgrades. Provisioned-instance volumes are independent and survive `docker compose down`.

**Schedule dumps** from the Backups page - they land in `./backups/`.

**Provisioned containers** are labelled as a Compose project named `commandblock-databases`, so Docker Desktop (and `docker compose ls`) groups them into their own cluster - separate from CommandBlock's own stack - instead of listing them as loose containers.

---

## Troubleshooting

<details>
<summary><strong>Common errors &amp; fixes</strong></summary>

| Symptom | Fix |
| --- | --- |
| `Vault:MasterKey must decode to 32 bytes` | Regenerate with `openssl rand -base64 32`. |
| `401 invalid_token: issuer is invalid` | `Oidc__Authority` must match the IdP's `issuer` byte-for-byte. |
| `401: signature key was not found` | API can't reach JWKS - check `Oidc__InternalAuthority` / `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`. |
| "You're not allowed to access this service" | IdP authenticated the user but the client policy denies them - allow the user/group. |
| CORS error on `/api/*` | `Cors__AllowedOrigins__0` must match the SPA origin (no trailing slash). |
| `Cannot connect to the Docker daemon` | The `/var/run/docker.sock` bind is missing from the `commandblock` service. |
| `<engine> container did not become ready` on provision | CommandBlock reaches provisioned DBs over its own Docker network (the default compose network) and falls back to the host port - keep `extra_hosts: ["host.docker.internal:host-gateway"]` on the commandblock service for that fallback. |
| `SocketException (13): Permission denied` | Running as non-root without socket access - keep the default root user or `group_add` the docker GID. |
| `No free host port in range` | `commandblock.yaml` `port_ranges` exhausted - delete an instance or widen the range. |
| DB auth fails after changing the password | `POSTGRES_PASSWORD` only applies on first init - reset in-DB or wipe the volume. |

</details>

---

See also: [Declarative instances](./declarative-instances.md) · [Nodes](./nodes.md) · [Developer setup](./dev-setup.md)
