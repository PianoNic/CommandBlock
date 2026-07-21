# Wake & sleep

A server that nobody is playing on is still holding RAM. CommandBlock can stop an idle server and start it again the moment someone tries to join, so a box that hosts six servers doesn't have to run six servers.

Both halves are per server, under **Settings → Wake & sleep**.

## Sleep when idle

Enable **auto-sleep** and give it an idle window. Once a server has had no routed connections for that long, CommandBlock stops the container. Its world, settings and container config are untouched - only the process goes away.

An idle server that has been stopped this way reads as **sleeping** in the UI, not offline: it's waiting for a player, not broken. A container that exited on its own with a failure code reads as **crashed** instead, so a server that died at 3am doesn't quietly look like it went to sleep.

## Wake on join

With **wake on join** enabled, a login attempt against a stopped server starts it. What the joining player sees while it boots depends on the mode:

| Mode | What happens |
| --- | --- |
| **Hold them & let them in automatically** | The player waits at the connecting screen (or in a limbo world) and is dropped into the server the moment it accepts players. No reconnect. |
| **Ask them to reconnect in a moment** | The player is immediately disconnected with "starting up, give it a moment". The server still boots in the background. |

Status pings are answered either way, so the server list shows a "sleeping - join to wake it up" MOTD rather than a dead entry.

### The hold window

The hold mode has a per-server budget - how long CommandBlock is willing to keep someone waiting before giving up and asking them to reconnect. Set it to `0` to get the reconnect message with no waiting at all.

Aim it above your server's typical boot time. The **Connections** page reports the p95 wake time measured from your actual servers, which is the number to size this against.

::: warning Clients older than 1.13
Holding a client open needs the login plugin-request packet, which arrived in 1.13 (protocol 393). Older clients have no such packet, so they can only be held silently and their own ~30s login timeout applies - the hold is capped near 25s for them regardless of what you set.
:::

## How the hold works

Two mechanisms, picked automatically per connection.

**The limbo waiting room.** If CommandBlock has a snapshot matching the client's protocol version, the player is placed in a tiny "server is starting" world - a real world they're actually logged into, with a message rather than a frozen connecting screen. When the backend comes up they're sent a Transfer packet and rejoin the real server on their own. Transfer arrived in 1.20.5 (protocol 766), and modded servers are skipped because their clients expect a mod-list negotiation the limbo can't reproduce.

Snapshots are captured automatically. A background sweep looks at running servers every five minutes and records a snapshot for any protocol version it doesn't have yet, so the limbo works on whatever versions you actually host instead of one version baked into the image. Capture logs a probe player in, so it only runs against servers with nobody connected.

**The login hold.** Everything else - modded servers, versions with no snapshot - is held in the login phase instead. CommandBlock stalls the handshake, keeps the client alive with periodic login plugin requests, and once the backend is up it replays the buffered login and splices the two sockets together. No game protocol is interpreted at any point, which is exactly why this path works on every version and every mod loader, Forge and NeoForge included.

Either way the player ends up on the real server, authenticated normally - CommandBlock never puts a backend into offline mode.

## Watching it work

The **Connections** page shows what the router is doing:

- **Live now** - who is connected, to which server, from where, and for how long.
- **Joins per hour** and **traffic by server** - where the load actually goes.
- **Wake on join** - how many wakes, the typical (p50) and slow (p95) time to ready, and how many failed. Size your hold window off the p95.
- **Turned away** - joins CommandBlock refused, and why: address not routed here, server offline with wake disabled, asked to reconnect, or gave up waiting for boot.

These are read from a rolling in-memory buffer rather than a database, so they cover the period since CommandBlock last started - the page says from when.

## Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| `Router__MaxHoldSeconds` | `180` | Global ceiling on the hold. A per-server window larger than this is capped to it. |
| `Router__BackendConnectTimeoutSeconds` | `2` | How long to wait when dialling a server before treating it as asleep. |

---

See also: [Servers](./servers.md) · [Hostname routing](./routing.md)
