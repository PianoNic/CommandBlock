# Servers & modpacks

A server is one `itzg/minecraft-server` container that CommandBlock creates, starts, and routes to.

## Create a server

From **Servers → Create server**, fill in:

| Field | Notes |
| --- | --- |
| **Type** | The loader: `VANILLA`, `PAPER`, `PURPUR`, `FABRIC`, `QUILT`, `FORGE`, `NEOFORGE`, `SPIGOT`, or a modpack installer (`MODRINTH`). Maps to the itzg `TYPE`. |
| **Memory** | Java heap / container memory, e.g. `4G`. |
| **Display name** | Shown in the UI. |
| **Hostname** | The address players connect with, e.g. `smp.example.com`. Must be unique - it's the router's key. |
| **Version** | Minecraft version for plain loaders (blank = latest). Hidden for modpack types. |
| **Modpack reference** | For modpack types: a Modrinth slug, a `.mrpack` URL, or a CurseForge ref. |

On create, CommandBlock pulls the image, starts the container on its Docker network with **no published port**, and stamps a `mc.host` label the router uses. Modpacks download the server side of the pack on first boot, so the server may take a few minutes to accept connections.

## What "modpack reference" means

For a public Modrinth pack, use the slug from its URL:

```
https://modrinth.com/modpack/cobblemon-fabric
                              └────── slug ──────┘
```

itzg installs the **server** side of that pack automatically - you don't upload your client instance. A self-built pack has no slug; export it as a `.mrpack` and pass its URL instead.

## Lifecycle

Each server row supports **Start**, **Stop**, and **Delete**. Delete removes the container and its world data (host folder or volume, per your [storage setting](./self-host#storage)).

## Connecting

Add each server in the Minecraft client by its hostname (e.g. `smp.example.com`). See [Hostname routing](./routing) for the DNS + firewall setup.
