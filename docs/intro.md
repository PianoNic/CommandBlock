# What is CommandBlock?

CommandBlock is a self-hosted manager for Minecraft (Java) servers. Pick a loader or a modpack, click Create, and you get a running server in an isolated container - reachable by its own hostname through a single public port.

- **Create in one click**. Vanilla, Paper, Purpur, Fabric, Quilt, Forge, NeoForge, Spigot, or a Modrinth/CurseForge/FTB modpack (installed on first boot).
- **One port, many servers**. Every server is reached on 25565. The built-in router reads the address from the Minecraft handshake and forwards to the matching server - so `smp.example.com` and `modded.example.com` share one open port.
- **World backups**. Snapshot a server's world (flushed via RCON for a clean copy) straight into an S3-compatible bucket such as SeaweedFS, and restore it in place.
- **Lifecycle controls**. Start, stop, and delete servers from the UI; live container state at a glance.
- **Bring your own auth**. OIDC with any provider (Pocket ID, Authentik, Auth0…), or the bundled mock server for local development.

## Architecture

Each server is provisioned as an isolated **sibling container** (`itzg/minecraft-server`) on the Docker host - not a child of CommandBlock - which is why CommandBlock mounts the Docker socket. Provisioned servers join CommandBlock's Docker network and publish **no** host port of their own; only the router's port is exposed.

```
Player --TCP 25565--> Router (in CommandBlock) --> mc-container (by hostname)
```

The control plane is the whole app: the Angular UI, the ASP.NET Core API, its metadata database (SQLite or Postgres), and the router.

## Distributions

CommandBlock ships two ways from the same codebase.

| Distribution | Metadata DB | Auth | Use case |
| --- | --- | --- | --- |
| **Docker image** | Postgres or SQLite | OIDC (bring-your-own or a bundled mock server) | Servers, multi-user, always-on |
| **Desktop app** | SQLite | Built-in, zero-config | A single user on their own machine |

## Get started

- **[Self-hosting](./self-host)** - run the image with `docker compose`.
- **[Servers & modpacks](./servers)** - create and manage servers.
- **[Hostname routing](./routing)** - how one port serves many servers.
- **[Developer setup](./dev-setup)** - local dev with `dotnet run` + Bun.
