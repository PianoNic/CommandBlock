# Self-host CommandBlock

Run CommandBlock from the pre-built image, on either registry:

- **GitHub Container Registry** - `ghcr.io/pianonic/commandblock:latest`
- **Docker Hub** - `pianonic/commandblock:latest`

The two are identical; use whichever you prefer. Examples below use the GHCR tag.

You need a Linux/Windows host with **Docker + Compose v2**, and a directory to keep state in.

## Quickstart

Drop these files in an empty folder and run `docker compose up -d`. Open the UI at <http://localhost:5000>; players connect on port **25565**.

**`compose.yml`**

```yaml
services:
  commandblock:
    image: ghcr.io/pianonic/commandblock:latest   # or pianonic/commandblock:latest (Docker Hub)
    container_name: commandblock
    restart: unless-stopped
    extra_hosts:
      - "host.docker.internal:host-gateway"
    depends_on:
      db:
        condition: service_healthy
      seaweedfs:
        condition: service_started
    ports:
      - "5000:8080"        # web UI / API
      - "25565:25565"      # the Minecraft router - the ONLY game port you open
    environment:
      Database__Provider: "Postgres"
      ConnectionStrings__CommandBlockDatabase: "Host=db;Port=5432;Database=commandblock;Username=postgres;Password=${POSTGRES_PASSWORD}"
      CommandBlock__PublicUrl: ${CommandBlock_PUBLIC_URL}
      Cors__AllowedOrigins__0: ${CommandBlock_PUBLIC_URL}
      Oidc__Authority: ${CommandBlock_OIDC_AUTHORITY}
      Oidc__ClientId: ${CommandBlock_OIDC_CLIENT_ID}
      Oidc__Scope: "openid profile email roles"
      Oidc__RequireHttpsMetadata: "true"
      # Backups -> SeaweedFS (S3)
      Backup__Enabled: "true"
      Backup__S3Endpoint: "http://seaweedfs:8333"
      Backup__Bucket: "commandblock-backups"
      Backup__AccessKey: "commandblock"
      Backup__SecretKey: ${SEAWEEDFS_SECRET}
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock   # Windows: //var/run/docker.sock
      - ./commandblock.yaml:/app/commandblock.yaml:ro
      # Server world data - a host folder mounted at the SAME path so the daemon and CommandBlock agree on it.
      - /data/servers:/data/servers

  db:
    image: postgres:18.4
    container_name: commandblock-db
    restart: unless-stopped
    environment:
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: commandblock
    volumes:
      - ./data/postgres:/var/lib/postgresql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d commandblock"]
      interval: 2s
      timeout: 3s
      retries: 30

  seaweedfs:
    image: chrislusf/seaweedfs:latest
    container_name: commandblock-seaweedfs
    restart: unless-stopped
    command: "server -s3 -dir=/data"
    environment:
      AWS_ACCESS_KEY_ID: commandblock
      AWS_SECRET_ACCESS_KEY: ${SEAWEEDFS_SECRET}
    volumes:
      - ./data/seaweedfs:/data
```

**`.env`**

```env
POSTGRES_PASSWORD=change-me
SEAWEEDFS_SECRET=change-me-too

# The public URL the web UI is served on. The login redirect URI and CORS origin are derived from it.
CommandBlock_PUBLIC_URL=http://localhost:5000

# Your OIDC provider:
CommandBlock_OIDC_AUTHORITY=https://auth.example.com/realms/commandblock
CommandBlock_OIDC_CLIENT_ID=commandblock
```

**`commandblock.yaml`** - where server worlds live on disk:

```yaml
commandblock:
  storage:
    mode: HostFolder        # worlds live in /data/servers/<container> on the host (set mode: Volume for named docker volumes)
    host_path: /data/servers
```

On your IdP, register CommandBlock as a **public client** (PKCE, no secret) with redirect URI `http://localhost:5000/*`.

::: tip No OIDC provider yet?
Clone the repo and run its `compose.yml` - it bundles a mock OAuth2 server so you can log in immediately for local testing. See [Developer setup](./dev-setup).
:::

## Networking

- **DNS**: point your hostnames at the host IP. A wildcard `A` record (`*.example.com`) covers every server with no per-server DNS. See [Hostname routing](./routing).
- **Firewall**: open **25565/tcp** for players and your UI port (proxied over HTTPS in production). Provisioned servers open no ports of their own unless you [publish one](./routing#direct-access-without-the-router).

## Storage

Server worlds default to a **host folder** (`storage.mode: HostFolder`, `host_path: /data/servers`) so the files are directly inspectable and backup-able. The same path is bind-mounted into the CommandBlock container at the identical path so `Delete server` can clean up the folder. Set `mode: Volume` to use per-server Docker named volumes instead.

::: warning
With `HostFolder`, `/data/servers` must be writable by the server containers. `itzg/minecraft-server` runs as UID 1000 by default - `sudo chown -R 1000:1000 /data/servers` if a server can't write its world.
:::

## Configuration reference

<details>
<summary><strong>Environment variables</strong></summary>

| Variable | What it does |
| --- | --- |
| `ConnectionStrings__CommandBlockDatabase` | CommandBlock's own metadata DB. Postgres: `Host=db;Port=5432;Database=commandblock;Username=postgres;Password=…`. SQLite: `Data Source=/data/commandblock.db`. |
| `Database__Provider` | `Postgres` or `Sqlite` (default). |
| `Oidc__Authority` / `Oidc__ClientId` / `Oidc__Scope` / `Oidc__RequireHttpsMetadata` | OIDC login (public/PKCE client). `Authority` must match the IdP `issuer` byte-for-byte. |
| `Oidc__RedirectUri` / `…PostLogoutRedirectUri` | Return URLs after login/logout (derived from `CommandBlock__PublicUrl` if unset). |
| `Cors__AllowedOrigins__0` | Browser origin allowed to call the API - UI URL **without** trailing slash. |
| `Router__ListenPort` / `Router__Enabled` / `Router__HandshakeTimeoutSeconds` | The Minecraft router (defaults: `25565`, `true`, `5`). |
| `Router__MaxHoldSeconds` / `Router__BackendConnectTimeoutSeconds` | How long a joining player may be held while their server wakes, and the backend dial timeout (defaults: `180`, `2`). See [Wake & sleep](./wake). |
| `Backup__Enabled` / `Backup__S3Endpoint` / `Backup__Bucket` / `Backup__AccessKey` / `Backup__SecretKey` / `Backup__Region` | Backups to S3/SeaweedFS. See [Backups](./backups). |
| `Docker__Endpoint` | Docker daemon URI. Optional - auto-detected when unset. |

</details>

<details>
<summary><strong>SQLite instead of Postgres</strong></summary>

Drop the `db` service and point CommandBlock at a file on a mounted host folder:

```yaml
    environment:
      Database__Provider: "Sqlite"
      ConnectionStrings__CommandBlockDatabase: "Data Source=/data/commandblock.db"
    volumes:
      - ./data/app:/data
```

Remove `depends_on: db`. The file **must** be on a mounted folder or it's wiped on every recreate.

</details>

<details>
<summary><strong>Reverse proxy (Caddy/Traefik/nginx)</strong></summary>

Proxy only the **web UI** over HTTPS. The Minecraft router (25565) is raw TCP - it does **not** go through an HTTP reverse proxy; expose that port directly.

```caddy
commandblock.example.com { reverse_proxy commandblock:8080 }
```

```env
Oidc__RedirectUri=https://commandblock.example.com/
Oidc__PostLogoutRedirectUri=https://commandblock.example.com/
Oidc__RequireHttpsMetadata=true
Cors__AllowedOrigins__0=https://commandblock.example.com
```

</details>

## Operations

**Upgrade**

```bash
docker compose pull commandblock && docker compose up -d commandblock
```

Migrations run on startup; the metadata DB, worlds, and running server containers are preserved. Pin a version by replacing `:latest` with a [published tag](https://github.com/PianoNic/CommandBlock/pkgs/container/commandblock).

**Back up** the `./data/postgres` (metadata) directory and your `commandblock-backups` bucket. World data lives under `/data/servers`.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `401 invalid_token: issuer is invalid` | `Oidc__Authority` must match the IdP's `issuer` byte-for-byte. |
| CORS error on `/api/*` | `Cors__AllowedOrigins__0` must match the UI origin (no trailing slash). |
| `Cannot connect to the Docker daemon` | The `/var/run/docker.sock` bind is missing from the `commandblock` service. |
| Player can't connect / "Can't resolve hostname" | DNS record missing or (Cloudflare) proxied - use a `DNS only` record. See [Routing](./routing). |
| Server can't write its world | `chown -R 1000:1000 /data/servers` (itzg runs as UID 1000). |
| Backups fail | Check `Backup__*` values and that the S3 endpoint is reachable from the container. |

---

See also: [Servers](./servers.md) · [Wake & sleep](./wake.md) · [Backups](./backups.md) · [Hostname routing](./routing.md) · [Developer setup](./dev-setup.md)
