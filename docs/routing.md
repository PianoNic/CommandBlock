# Hostname routing

CommandBlock fronts every server with one TCP port (25565) and routes each incoming connection to the right server by the **address the player typed** - no per-server port.

## How it works

The first packet a Minecraft (Java) client sends is the handshake, which carries the server address it's connecting to. CommandBlock's router:

1. Accepts the raw TCP connection on 25565.
2. Reads only the handshake and extracts the address (e.g. `smp.example.com`).
3. Looks it up against each server's hostname.
4. Connects to that server's container over the shared Docker network, replays the handshake, and then pipes bytes both ways.

Unknown hostnames and malformed/half-open connections are dropped, which also swallows most port scanners.

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

Open only **25565/tcp**. Provisioned servers publish no port of their own - they're reached solely through the router - so that single port is the whole surface.

## Configuration

| Setting | Default | Notes |
| --- | --- | --- |
| `Router__Enabled` | `true` | Turn the listener off entirely. |
| `Router__ListenPort` | `25565` | The single public port. |
| `Router__HandshakeTimeoutSeconds` | `5` | How long a client has to send its handshake before it's dropped. |

::: warning Java Edition only
Hostname routing relies on the Java handshake. Bedrock (UDP) is not routed.
:::
