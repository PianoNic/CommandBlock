# Servers

A server is one `itzg/minecraft-server` container that CommandBlock creates, starts, and routes to.

## Create a server

From **Servers → Create server**, fill in:

| Field | Notes |
| --- | --- |
| **Type** | The loader: `VANILLA`, `PAPER`, `PURPUR`, `FABRIC`, `QUILT`, `FORGE`, `NEOFORGE` or `SPIGOT`. Maps to the itzg `TYPE`. |
| **Memory** | Java heap / container memory. Pick from the slider or type an exact value like `6G`. The slider is bounded by the host's real memory. |
| **Display name** | Shown in the UI. |
| **Hostname** | The address players connect with, e.g. `smp.example.com`. Must be unique - it's the router's key. |
| **Version** | Minecraft version (blank = latest release). |

Only versions Mojang actually publishes a **server** jar for are offered. Releases before 1.2.5 are client-only, and picking one used to leave the container crash-looping on a download that doesn't exist.

Under **Advanced** you can also set the Java version, Aikar's JVM flags (on by default for 1.21 and newer, where they help), extra JVM args, arbitrary itzg env vars, and the any-client-version toggle below.

On create, CommandBlock pulls the image, starts the container on its Docker network with **no published port**, and stamps a `mc.host` label the router uses.

## Letting any client version join

**Allow any client version** installs the Via stack (ViaVersion + ViaBackwards + ViaRewind) into the server, so a 1.8 client and a current one can join the same backend. It's available at create time and in the settings.

This only translates the game protocol - authentication is untouched, so the server stays in online mode and player UUIDs are unchanged.

::: warning Servers older than 1.20
The Via stack is skipped on servers older than 1.20: itzg aborts startup when there's no matching download, so enabling it there would prevent the server from booting at all.
:::

## Lifecycle

Start, stop and restart from the dashboard card, the server row, or the detail page. **Delete** removes the container and its world data (host folder or volume, per your [storage setting](./self-host#storage)).

The detail page shows live vitals - status, uptime, CPU, memory, players online, version, type and connection address - plus an embedded console wired to RCON.

## Settings

Each server has a **Settings** modal (from the row's kebab menu or the detail header):

- **General / MOTD** - the most-used `server.properties` with a live MOTD editor, plus the server's display name.
- **Runtime** - memory, Java version, Aikar/JVM flags, and extra itzg env vars (applied on the next restart).
- **Version** - change the Minecraft version. The container is recreated in place and the world is kept.
- **Wake & sleep** - start on join and auto-sleep when idle, per server. See [Wake & sleep](./wake).
- **Icon** - upload a PNG; it's cropped to the 64×64 server-icon and shown in the UI and in-game.

::: tip Back up before changing version
Recreating keeps the world, but Minecraft's world format only upgrades forward - moving a world to an older version can corrupt it. Take a [backup](./backups) first.
:::

## Backups

Snapshot a server's world, or take a full-server backup you can restore or clone a new server from. See [Backups](./backups).

## Connecting

Add each server in the Minecraft client by its hostname (e.g. `smp.example.com`). See [Hostname routing](./routing) for the DNS + firewall setup.
