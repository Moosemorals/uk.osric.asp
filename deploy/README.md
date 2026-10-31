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
  Caddyfile                 site config, installed to /etc/caddy/Caddyfile
```

The quadlets and the Caddyfile are the source of truth for the server: `make deploy` copies them up
and applies them.

## First deployment

1. Create the VM (Hetzner CX22, Debian 13) with the official CLI image — nothing is installed locally
   except podman. `--ssh-key` takes the *name* of a key registered in the Hetzner project, not a path
   to a local file, so register it first. `cloud-init.yaml` already lists the two keys `deploy` will
   accept (the ones on `lucy` and `Carrot`); edit that list if you deploy from another machine. The
   Hetzner key is a fallback for root if user creation ever fails.

   ```bash
   export HCLOUD_TOKEN=<token from the Hetzner console>

   podman run --rm -e HCLOUD_TOKEN \
     -v "$HOME/.ssh/id_ed25519.pub:/keys/id_ed25519.pub:ro" \
     docker.io/hetznercloud/cli:latest \
     ssh-key create --name lucy --public-key-from-file /keys/id_ed25519.pub

   podman run --rm -e HCLOUD_TOKEN \
     -v "$PWD/deploy:/deploy:ro" \
     docker.io/hetznercloud/cli:latest \
     server create --name uk.osric.asp --type cx22 --image debian-13 \
       --location fsn1 --ssh-key lucy \
       --user-data-from-file /deploy/cloud-init.yaml
   ```

   The image is a distroless binary whose entrypoint is `hcloud`, so arguments are passed straight
   through; add `:z` to the mounts on a host with SELinux enforcing.

   `cloud-init.yaml` installs podman with the rootless pieces (`uidmap`, `passt`, `fuse-overlayfs`),
   creates the `deploy` user with subuid/subgid ranges and its own SSH keys, enables lingering so its
   user manager outlives the login, hardens SSH, and opens 22/80/443 in ufw. It reboots once if the
   package upgrade installed a new kernel, and ends with a rootless `podman info` that must print
   `true` — check it with `cloud-init status --long` before relying on the host.

2. Point an `A` (and `AAAA`, if you use IPv6) record at the server.

3. Put your domain and email in `deploy/Caddyfile`. `make deploy` refuses to run while the
   `example.com` placeholder is still there, because Caddy would start, fail its ACME challenge, and
   leave the site unreachable while every command reports success.

4. Log in to the registry locally and on the server — see [The registry](#the-registry). `make deploy`
   pushes as your local user and pulls as `deploy`, so both need credentials that outlive a reboot.

5. Ship it:

   ```bash
   make deploy HOST=deploy@<server-ip>
   ```

   That builds and pushes the image, pulls it as `deploy`, installs the user quadlet, writes the
   Caddyfile to `/etc/caddy/` with sudo, reloads the user manager, then installs the Caddy unit,
   reloads systemd and restarts both services. Caddy obtains a certificate on first request, so the
   site is reachable a few seconds later.

## Day-to-day

- **New code:** back up storage first, then `make deploy HOST=deploy@<server-ip>` (builds, pushes,
  pulls, restarts).
- **Domain or TLS settings:** edit `deploy/Caddyfile`, then `make deploy HOST=...`.
- **Reserved page keys:** `Pages:ReservedPaths` (environment form
  `Pages__ReservedPaths__0`, `__1`, ...) lists the keys the site's own routes answer for. The default
  covers `/edit`, `/site.css`, `/favicon.ico` and `/heath`; change it only when the route table
  changes.
- **Health:** `ssh deploy@<server-ip> curl -s http://127.0.0.1:8080/heath` returns detailed JSON —
  200 when the app can reach its database, 503 otherwise.
- **Logs:** `ssh deploy@<server-ip> journalctl --user -u uk.osric.asp.service -f`
- **Status:** `ssh deploy@<server-ip> systemctl --user status uk.osric.asp.service`
- **Edge:** `ssh deploy@<server-ip> sudo systemctl status caddy`
- **Rollback:** build the previous commit and deploy it again; images are replaced, not accumulated.
  A previous image may not support the migrated schema; restore its matching backup with the app
  stopped when necessary.

## Storage and migrations

Source storage uses EF Core with SQLite. The local default is `ConnectionStrings:Storage` set to
`Data Source=data/storage.db`; the app quadlet overrides it with
`ConnectionStrings__Storage='Data Source=/data/storage.db'`. Route-to-script lookup, the compiled
image of each page and editor persistence all read and write this database; nothing else in the
deployment depends on it.

Each page row carries the assembly emitted when it was last saved, so the database grows by roughly
the size of each page's assembly (tens of kilobytes per page) on top of its source. A release that
changes `uk.osric.asp.Core` changes the runtime id stored beside every image, so each page is
compiled again on its next request: the first visit after such a deploy is slower, once, and the
row is rewritten in place. There is no startup warm-up, so a page nobody visits is not recompiled
until someone asks for it.

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
with the model change. `make test` includes the Web storage and integration tests as well as the core
and runtime tests.

### Backups

Run storage maintenance as `deploy`. Either stop `uk.osric.asp.service` with `systemctl --user stop`
and back up the entire volume while no process has the database open, or use SQLite's online backup
API against the database — `sqlite3` is installed on the host by `cloud-init.yaml`, so
`sqlite3 storage.db '.backup /tmp/storage-backup.db'` works from inside `podman unshare`. Do not
simply copy `storage.db` while the app is running: committed data may still be in the WAL, and
copying database files individually can produce an inconsistent backup.

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

## Proxy trust and forwarded headers

Caddy terminates TLS, so without help the app sees a plain-HTTP request arriving from the loopback
proxy: `Request.IsHttps` is false and `UseHsts` emits nothing. It therefore calls `UseForwardedHeaders`
for `X-Forwarded-Proto` and `X-Forwarded-Host`, which Caddy sets by default, and trusts those headers
from any peer.

That is safe only because of the topology. The quadlet publishes the port on `127.0.0.1` alone, so
nothing off the machine can reach the app, and the only client that can send those headers is the local
Caddy. **The loopback-only publish is the control:** do not widen `PublishPort`, and do not place
another proxy in front that rewrites the headers without revisiting this decision.

What it changes:

- HSTS works: `UseHsts` only sends `Strict-Transport-Security` for requests it believes are HTTPS.
- `Request.Scheme`, `Request.Host` and `Request.IsHttps` describe the browser's request, so cookies,
  absolute URLs and redirects derived from them are correct.
- Caddy redirects HTTP to HTTPS itself; the app deliberately has no `UseHttpsRedirection`, which behind
  a proxy is a redirect loop waiting to happen.

If the app ever runs somewhere else — another proxy, a load balancer, or a directly published port —
replace the cleared `KnownProxies`/`KnownIPNetworks` with that proxy's address instead of trusting
every peer.

## Image layout and size

Every image goes through `registry.osric.uk`, so the layout is chosen for layers:

| base | app layer per deploy | image |
| --- | --- | --- |
| `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` | 177 kB (framework-dependent) | 122 MB |

The ~120 MB .NET runtime lives in the base image's layers, which are pulled once and cached by both
the registry and the host. Each release therefore pushes and pulls only the app layer. Building it as a
self-contained single file instead would make the image smaller (64 MB) but would move ~52 MB on every
release, which is the wrong trade when a registry is available.

Trimming would reach ~29 MB, but ASP.NET Core does not support trimming a hosted web app here: the
trimmed image dies at startup with `TypeLoadException`. It is deliberately off.

## The registry

`REGISTRY` defaults to `registry.osric.uk`; `make deploy` tags the locally built image, pushes it, and
tells the server to pull:

```bash
podman login --authfile ~/.config/containers/auth.json registry.osric.uk            # locally
ssh -t deploy@<server-ip> podman login --authfile /home/deploy/.config/containers/auth.json registry.osric.uk
make deploy HOST=deploy@<server-ip>
```

The server path is written out in full: an unquoted `~` in an `ssh` command is expanded by the
**local** shell, so it would point at the wrong home.

The `--authfile` on the server is not optional: podman's default is
`${XDG_RUNTIME_DIR}/containers/auth.json`, which lives under `/run/user/` and is gone after a reboot,
so a pull would fail on the first deploy after every restart. `make deploy` reads the same file
(`AUTHFILE` locally, `REMOTE_AUTHFILE` on the server), so override those variables if you keep the
credentials somewhere else. If the registry serves a certificate the host does not trust, install the
CA at `/etc/containers/certs.d/registry.osric.uk/ca.crt` (or the same path under
`~/.config/containers/certs.d/`).

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
