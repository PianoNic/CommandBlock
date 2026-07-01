---
layout: home

hero:
  name: CommandBlock
  text: Every server. Routed and ready.
  tagline: One click. A self-hosted Minecraft server manager with modpacks and hostname routing through a single port.
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
    details: Vanilla, Paper, Fabric, Forge, NeoForge and more - or a Modrinth/CurseForge modpack, installed on first boot.
  - title: One port, many servers
    details: Players reach every server on 25565; CommandBlock routes by the hostname in the Minecraft handshake.
  - title: World backups to S3
    details: Snapshot a server's world (RCON-flushed) and store it in SeaweedFS or any S3-compatible bucket. Restore in place.
  - title: Bring your own auth
    details: OIDC with any provider (Pocket ID, Authentik, Auth0…), or the bundled mock server for local dev.
---
