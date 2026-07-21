# Hostname routing

CommandBlock fronts every server with one TCP port (25565) and routes each incoming connection to the right server by the **address the player typed** - no per-server port.

## How it works

The first packet a Minecraft (Java) client sends is the handshake, which carries the server address it's connecting to. CommandBlock's router:

1. Accepts the raw TCP connection on 25565.
2. Reads only the handshake and extracts the address (e.g. `smp.example.com`).
3. Looks it up against each server's hostname.
4. Connects to that server's container over the shared Docker network, replays the handshake, and then pipes bytes both ways.

Unknown hostnames and malformed/half-open connections are dropped, which also swallows most port scanners.

If the matching server is stopped, the router doesn't just fail the connection: it can start the server and keep the player waiting until it's ready. See [Wake & sleep](./wake).

The **Connections** page shows every connection the router is handling, along with traffic, wake timings and the joins it turned away.

## Direct access without the router

Routing is the default because it keeps the host down to one open game port. A server can also be
published on a host port of its own - handy on a LAN, where you'd rather type `192.168.1.50:25566`
than set up DNS.

Under **Settings → Network**, per server:

| Option | What it does |
| --- | --- |
| **Reachable through the router by hostname** | Whether the router answers for this server's hostname. Turn it off and the router treats the hostname as unknown, so the server is reachable only on its own port. |
| **Publish a port on the host** | Binds the container's game port to a host port, reached as `<host-ip>:<port>`. |
| **Bind to address** | Which host interface that port listens on. Empty means all of them. |

The two are independent: a server can be on the router, on its own port, or both. It can't be on
neither - CommandBlock rejects that rather than leaving a server nothing can reach.

::: warning "LAN only" means binding a LAN address
An empty bind address listens on **every** interface, so on an internet-facing host the port is
reachable from anywhere the firewall allows - it does not pass through the router. To keep a server on
the local network, bind it to a private address (e.g. `192.168.1.50`), or `127.0.0.1` for the host alone.
:::

Publishing is fixed when the container is created, so changing the port or its bind address recreates
the container. The world is bind-mounted by name and survives.

## DNS

Point the hostnames at your host's IP. A **wildcard** record is easiest - one record covers every server:

```
*.example.com   A   <your-server-ip>
```

Then each new server just needs a unique hostname in CommandBlock; DNS needs no further changes. No `SRV` record is required, because the router listens on the standard port.

::: tip Cloudflare
Set the record to **DNS only** (grey cloud). Cloudflare's proxy only handles HTTP/HTTPS, not the Minecraft TCP protocol.
:::

## Firewall

Open only **25565/tcp**. Provisioned servers publish no port of their own unless you give them one, so that single port is the whole surface by default.

## Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| `Router__Enabled` | `true` | Turn the listener off entirely. |
| `Router__ListenPort` | `25565` | The single public port. |
| `Router__HandshakeTimeoutSeconds` | `5` | How long a client has to send its handshake before it's dropped. |
| `Router__MaxHoldSeconds` | `180` | Ceiling on how long a joining player is held while their server boots. Caps the per-server window. |
| `Router__BackendConnectTimeoutSeconds` | `2` | How long to wait when dialling a server before treating it as asleep. |

::: warning Java Edition only
Hostname routing relies on the Java handshake. Bedrock (UDP) is not routed.
:::
