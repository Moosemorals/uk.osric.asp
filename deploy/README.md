<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Deployment

The app ships as an OCI image built by the SDK itself (`dotnet publish -t:PublishContainer`) and runs
under **podman quadlets** on a single Debian 13 (trixie) VM. There is no Dockerfile and no Docker.

The app runs **rootless** as the `deploy` user. **Caddy** is the only rootful unit: it owns 80/443,
uses host networking, and proxies to the app's loopback port.

```
deploy/
  cloud-init.yaml           one-shot host bootstrap (rootless podman, user, firewall, SSH)
  uk.osric.asp.container    user quadlet: the app, rootless, publishing 127.0.0.1:8080
  caddy.container           system quadlet: Caddy, host network, terminating TLS
  Caddyfile                 site config, mounted read-only into Caddy
```

The quadlets are the source of truth for the server: `make deploy` copies them up and applies them.

## First deployment

1. Create the VM (Hetzner CX22, Debian 13, your SSH key):

   ```bash
   hcloud server create --name uk.osric.asp --type cx22 --image debian-13 \
     --location fsn1 --ssh-key ~/.ssh/id_ed25519.pub \
     --user-data-from-file deploy/cloud-init.yaml
   ```

   `cloud-init.yaml` installs podman with the rootless pieces (`uidmap`, `passt`, `fuse-overlayfs`),
   creates the `deploy` user with subuid/subgid ranges, enables lingering so its user manager outlives
   the login, hardens SSH, and opens 22/80/443 in ufw. Edit the placeholder key first.

2. Point an `A` (and `AAAA`, if you use IPv6) record at the server.

3. Put your domain and email in `deploy/Caddyfile`.

4. Ship it:

   ```bash
   make deploy HOST=deploy@<server-ip>
   ```

   That builds and pushes the image, pulls it as `deploy`, installs the user quadlet and Caddyfile,
   reloads the user manager, then installs the Caddy unit with sudo and reloads systemd. Caddy obtains
   a certificate on first request, so the site is reachable a few seconds later.

## Day-to-day

- **New code:** back up storage first, then `make deploy HOST=deploy@<server-ip>` (builds, pushes,
  pulls, restarts).
- **Domain or TLS settings:** edit `deploy/Caddyfile`, then `make deploy HOST=...`.
- **Logs:** `ssh deploy@<server-ip> journalctl --user -u uk.osric.asp.service -f`
- **Status:** `ssh deploy@<server-ip> systemctl --user status uk.osric.asp.service`
- **Edge:** `ssh deploy@<server-ip> sudo systemctl status caddy`
- **Rollback:** build the previous commit and deploy it again; images are replaced, not accumulated.
  A previous image may not support the migrated schema; restore its matching backup with the app
  stopped when necessary.

## Storage and migrations

Source storage uses EF Core with SQLite. The local default is `ConnectionStrings:Storage` set to
`Data Source=data/storage.db`; the app quadlet overrides it with
`ConnectionStrings__Storage='Data Source=/data/storage.db'`. This is source storage infrastructure
only: route-to-script lookup, editor persistence and runtime storage access are not wired up yet.

The rootless named volume `uk.osric.asp-storage` is mounted read-write at `/data`. Podman creates it
on first use and reuses it when the container or image is replaced. It belongs to the `deploy` user's
Podman storage, not root's. Removing the volume (including pruning unused volumes) or deleting the
host's storage destroys the data; container replacement is not a backup strategy.

The mount's `:U` option recursively changes the volume's ownership to the host UID/GID mapped from
the container's `User=1654:1654`. This lets the unprivileged app create the database and SQLite
journal/WAL files without relaxing permissions. It modifies ownership on the host and may slow
startup on a volume with many files. Keep this volume dedicated to the app; do not substitute a
shared host directory without accepting those ownership changes. See Podman's
[volume ownership documentation](https://docs.podman.io/en/latest/markdown/podman-run.1.html#volume-v).

The app applies pending migrations asynchronously at startup, before serving requests. **Take a
verified backup before starting a new release with migrations**, including unattended updates.
Review generated migrations before deployment; startup migration does not make a backup for you.

To add a migration locally from the repository root:

```bash
make migration NAME=DescribeTheSchemaChange
make test-web
```

The target restores the repository's pinned local `dotnet-ef` tool (10.0.14), builds using the normal
Makefile target, and writes migration sources to the Web project's `Storage/Migrations` directory.
EF 10 reads the evaluated MSBuild output paths, including `artifacts/obj/uk.osric.asp.Web/`; the old
`--msbuildprojectextensionspath` override is not needed. Commit and review the generated migration
with the model change. `make test` includes the Web storage tests as well as unit and specification
tests.

### Backups

Run storage maintenance as `deploy`. Either stop `uk.osric.asp.service` with `systemctl --user stop`
and back up the entire volume while no process has the database open, or use SQLite's online backup
API (for example the `sqlite3` shell's `.backup` command) against the database. Do not simply copy
`storage.db` while the app is running: committed data may still be in the WAL, and copying database
files individually can produce an inconsistent backup.

Use `podman volume inspect uk.osric.asp-storage` as `deploy` to locate the volume. Its files are
owned by mapped subordinate IDs, so offline file access may require `podman unshare`. Keep backups
outside the volume and off-host, and test restoration. Restore only with the app stopped, retain
the correct mapped ownership, and start an image compatible with the restored schema. For the
stop-and-copy approach, keep the app stopped until the backup finishes, then start it again with
`systemctl --user start uk.osric.asp.service`.

## Why rootless

The app is a user quadlet in `~deploy/.config/containers/systemd`, so podman runs it in a user
namespace: the container's uid 1654 maps to a subuid on the host, not to a bare host uid, and no part
of it needs root. Caddy is the exception because it binds 80/443; it runs rootful on the host network
and reaches the app at `127.0.0.1:8080`.

A rootless container cannot join a rootful podman network, which is why the app publishes to the
loopback and Caddy uses `Network=host` instead of a shared bridge.

## Image layout and size

Every image goes through `registry.osric.uk`, so the layout is chosen for layers:

| base | app layer per deploy | image |
| --- | --- | --- |
| `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` | 177 kB (framework-dependent) | 122 MB |

The ~120 MB .NET runtime lives in the base image's layers, which are pulled once and cached by both
the registry and the host. Each release therefore pushes and pulls only the app layer. Building it as a
self-contained single file instead would make the image smaller (64 MB) but would move ~52 MB on every
release, which is the wrong trade when a registry is available.

Trimming would reach ~29 MB, but ASP.NET MVC does not support it: the trimmed image dies at startup
with `TypeLoadException`. It is deliberately off.

## The registry

`REGISTRY` defaults to `registry.osric.uk`; `make deploy` tags the locally built image, pushes it, and
tells the server to pull:

```bash
podman login registry.osric.uk                    # locally, and as deploy on the server
make deploy HOST=deploy@<server-ip>
```

The first push uploads the base layers (~120 MB); later pushes send only the app layer. Rootless
images live in `~deploy/.local/share/containers`, so `podman images` as root will not show them.

For unattended updates, add `AutoUpdate=registry` to the app quadlet and run
`systemctl --user enable --now podman-auto-update.timer` on the server.

## Notes

- The image is built for `linux-musl-x64`. On a CAX (ARM) server, use
  `make deploy CONTAINER_RID=linux-musl-arm64`.
- The app container runs as uid 1654 with all capabilities dropped and `no-new-privileges`; only
  Caddy publishes ports that the outside world can reach.
- Data-protection keys are ephemeral, so restarting the container invalidates any issued cookies. If
  that matters, mount a volume at `/home/app/.aspnet` and it becomes persistent.
