# Skate 3 Blaze Server — Xbox 360 / Recomp

A Blaze / EA Nation revival server for the **Skate 3 native PC recompilation**
(the Xbox 360 build). It lets the recomp's built-in EA Nation client sign in and
play online against a self-hosted server instead of EA's (which were shut down
years ago).

This is a fork of [**skate6743/Skate3BlazeServer**](https://github.com/skate6743/Skate3BlazeServer)
— the original Skate 3 Custom Blaze Server (for PS3/RPCS3) — with **Xbox 360
login support** added so the recomp can connect. The original PS3/RPCS3 login
path is left intact, so the server serves both.

## Features

* Xbox 360 login (gamertag + XUID) for the Skate 3 recomp — sign-in and online freeskate
* Functional matchmaking and player invites from the in-game friends list
* Relay servers for each lobby (no peer-to-peer connections between players)
* Park uploading / downloading from the Skate.Park menu in-game

## Hosting

The server runs on Windows. A self-contained build needs nothing installed;
building from source needs the .NET 8 SDK.

Open (or port-forward) these on the host:

| Port | Protocol | Purpose |
| --- | --- | --- |
| 42100 | TCP | Blaze (redirector + main online session) |
| 80 | TCP | Web (login config, Skate.Feed, achievements) |
| 17000–17500 | UDP | Per-game relay (used once players join a lobby) |

1. In `settings.json`, either set `"LocalHost": false` to auto-detect and
   advertise the host's public IP, or set `"LocalHost": true` with
   `"LocalIPAddress"` set to a specific address (e.g. a LAN or Radmin VPN IP for
   playing with friends).
2. Run the server. **On Windows, run it as Administrator** — the web server
   binds port 80.
3. Point the recomp at it: in the game's `settings.toml`, set
   `skate3_blaze_server_ip` to the server's IP (the port,
   `skate3_blaze_server_port`, defaults to `42100`). Launch the game and go
   online.

Build from source:

```sh
dotnet build "Skate Custom Server/Skate Custom Server.csproj" -c Release
```

The SQLite database is created and migrated automatically on first run.

## The recomp

This server pairs with the Skate 3 native PC recompilation:
**https://github.com/DonaldDuck979/skate3recomp**

## Special Thanks

Forked from and built on [**skate6743/Skate3BlazeServer**](https://github.com/skate6743/Skate3BlazeServer),
the original Skate 3 Custom Blaze Server.

[@Aim4Kill](https://github.com/Aim4kill) for making the [BlazeSDK](https://github.com/Aim4kill/BlazeSDK) (Saved so much time with having the packet structures there for almost all Blaze commands)

[@gamingrobot](https://github.com/gamingrobot) for amazing documentation on the TDF format used in Blaze servers.

[New Blaze Emulator](https://github.com/Tratos/New-Blaze-Emulator) by [@Tratos](https://github.com/Tratos) (Example Blaze server with working matchmaking)

[NPTicket](https://github.com/LittleBigRefresh/NPTicket) by [LittleBigRefresh Team](https://github.com/LittleBigRefresh) (Used for RPCN ticket validation)

[BWKingsnake](https://github.com/bwkingsnake) for making the [Skate 3 Online Config Tool](https://github.com/bwkingsnake/rpcs3-skate-3-config-editor) for setting all needed config values.

## Disclaimer

Not affiliated, associated, authorized, endorsed by, or in any way officially connected with Electronic Arts Inc. or any of its subsidiaries or affiliates. The use of any trademarks, logos, or brand names is for identification purposes only and does not imply endorsement or affiliation.
