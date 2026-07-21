---
layout: home

hero:
  name: CommandBlock
  text: Every server. Routed and ready.
  tagline: One click. A self-hosted Minecraft server manager with hostname routing, wake-on-join and backups - all through a single port.
  image:
    src: /logo.svg
    alt: CommandBlock
  actions:
    - theme: brand
      text: Self-host CommandBlock
      link: /self-host
    - theme: alt
      text: Developer setup
      link: /dev-setup
    - theme: alt
      text: GitHub
      link: https://github.com/PianoNic/CommandBlock

features:
  - title: One-click servers
    details: Vanilla, Paper, Purpur, Fabric, Quilt, Forge, NeoForge and Spigot, on any Minecraft version that ships a server jar.
  - title: One port, many servers
    details: Players reach every server on 25565; CommandBlock routes by the hostname in the Minecraft handshake.
  - title: Backups & cloning
    details: World or full-server snapshots to SeaweedFS or any S3 bucket - restore in place, schedule with cron, or spin up a new server from a backup.
  - title: Bring your own auth
    details: OIDC with any provider (Pocket ID, Authentik, Auth0…), or the bundled mock server for local dev.
---
