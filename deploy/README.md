<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Deployment

The app ships as an OCI image built by podman from `deploy/Containerfile` and runs under **podman
quadlets** on a single Debian 13 (trixie) arm64 VM (Hetzner CAX21). The image contains the publish
output and nothing else: the SDK runs on the build machine, never in the image, and the build
context is the published directory, so `COPY . /app` is exactly the files the app runs from.

The build is reproducible. `podman build --timestamp` normalises the created time and every file
mtime, the epoch is the commit's own timestamp, and the base image is pinned by digest, so two builds
of one commit produce one image id — on the same buildah version, which is recorded in the image's
`io.buildah.version` label. `make image-check` proves it by building twice and comparing, and it runs
as part of `make ci`.

The app runs **rootless** as the `deploy` user, beside a second rootless container for **Prometheus**.
**Caddy** is the only rootful unit: it owns 80/443, uses host networking, and proxies to the app's
loopback port.

```
deploy/
  Containerfile             the image: the publish output on the pinned aspnet base
  cloud-init.yaml           one-shot host bootstrap (rootless podman, user, firewall, SSH)
  uk.osric.asp.container    user quadlet: the app, rootless, publishing 127.0.0.1:8080
  uk.osric.asp.network      user network: the app and Prometheus talking by name
  prometheus.container      user quadlet: Prometheus, rootless, publishing 127.0.0.1:9090
  prometheus.yml            scrape config, installed to ~/.config/prometheus/prometheus.yml
  caddy.container           system quadlet: Caddy, host network, terminating TLS
  Caddyfile                 site config, installed to /etc/caddy/Caddyfile
```

The quadlets and the Caddyfile are the source of truth for the server: `make deploy` copies them up
and applies them.

## First deployment

1. Create the VM (Hetzner `cax21`: 4 Arm64 vCPUs, 8 GB of RAM, 80 GB of SSD; Debian 13) with the
   official CLI image — nothing is installed locally except podman. `--ssh-key` takes the *name* of a key registered in the Hetzner project, not a path
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
     server create --name uk.osric.asp --type cax21 --image debian-13 \
       --location fsn1 --ssh-key lucy \
       --user-data-from-file /deploy/cloud-init.yaml
   ```

   The image is a distroless binary whose entrypoint is `hcloud`, so arguments are passed straight
   through; add `:z` to the mounts on a host with SELinux enforcing.

   `cloud-init.yaml` installs podman with the rootless pieces (`uidmap`, `passt`, `fuse-overlayfs`),
   creates the `deploy` user with subuid/subgid ranges and its own SSH keys, enables lingering so its
   user manager outlives the login, hardens SSH, and opens 22, 80 and 443 (TCP and UDP) in ufw. It
   also creates a 4 GiB swap file and adds it to `/etc/fstab` — see [memory and
   swap](#memory-and-swap) for what it is for. It reboots once if the package upgrade
   installed a new kernel, and ends with a rootless `podman info` that must print `true` — check it
   with `cloud-init status --long` and `swapon --show` before relying on the host.

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

   That runs the same gate as a commit (`make check`: formatting, licences, package audit, all four
   suites and a Release publish), builds and pushes the image, pulls it as `deploy`, installs the
   network and the app's user quadlet, restarts the app, writes the Prometheus config and quadlet and
   restarts that, then writes the Caddyfile to `/etc/caddy/` with sudo, installs the Caddy unit,
   reloads systemd and restarts Caddy. Caddy obtains a certificate on first request, so the site is
   reachable a few seconds later.

   The target refuses to run from a tree with uncommitted changes, because the image is tagged with
   its commit and that tag is only meaningful if the commit is the tree (`ALLOW_DIRTY=1` overrides,
   for a deliberate one-off). It runs the gate (`make check`) before the image is built, pushes the
   commit tag alongside `IMAGE_TAG`, and it does not report success until
   `http://127.0.0.1:8080/heath` answers: a restart that leaves the unit failed is a failed deploy,
   not a quiet one.

   The quadlet's `Image=` names the commit, so the host answers "what is running?" without anyone
   guessing, and rolling back is a tag rather than a rebuild.

## Day-to-day

- **New code:** commit it, then back up storage, then `make deploy HOST=deploy@<server-ip>`
  (guards, checks, builds, pushes, pulls, restarts, waits for `/heath`).
- **The gate:** `make check`, run by hand when you want it — `make deploy` runs it anyway. `make ci`
  is the slower version that starts from `make clean` and fails if the build dirties the tree; run it
  before a release, or from any machine that is asked to prove the tree. Neither needs a CI service,
  an account or a secret, and nothing runs them behind a `git push`.
- **Domain or TLS settings:** edit `deploy/Caddyfile`, then `make deploy HOST=...`.
- **Page assistant:** disabled by default. `make deploy` keeps an empty, mode `0600`
  `~/.config/uk.osric.asp/agent.env` for the `deploy` user, because the container cannot start
  without that file — podman's env file, which is what the quadlet's `EnvironmentFile=` becomes, has
  no optional form. Fill it in and redeploy to enable the agent: `Agent__Enabled=true`,
  `Agent__ApiKey=<provider-key>`, and optionally `Agent__Model` or `Agent__BaseUrl`. The deploy never
  overwrites an existing file. The configured provider must speak OpenAI-compatible streaming chat
  completions with tool calls. Local HTTP is allowed only for a loopback provider or when explicitly
  enabled.
  The host sends the system prompt, conversation transcript, and any page source read by the agent to
  that provider. Do not store content there that the provider must not receive. Prompts, page source,
  reasoning, credentials, and provider error bodies are not logged. The default provider is DeepSeek;
  compatible services and local Ollama can be selected by configuration. Restart the app after
  changing the environment file.
- **Assistant streaming through Caddy:** `/assistant/*/events` is excluded from compression and the
  reverse proxy flushes events immediately. Verify through the proxy with a real queued conversation
  using `curl -N`, not only against the Kestrel port. A run continues if its browser is closed; the
  `Stop` form cancels it. During graceful shutdown the host records unfinished runs as interrupted;
  the 600-second default run timeout is independent of the container's 35-second graceful stop.
- **Reserved page keys:** `Pages:ReservedPaths` (environment form
  `Pages__ReservedPaths__0`, `__1`, ...) lists the keys the site's own routes answer for. The default
  covers `/admin`, `/assistant`, `/edit`, `/favicon.ico`, `/heath`, `/login`, `/logout`, `/metrics` and `/status`;
  `/admin` reserves the namespace for authenticated site administration. Change it only when the
  route table changes.
- **Health:** `ssh deploy@<server-ip> curl -s http://127.0.0.1:8080/heath` returns detailed JSON —
  200 when the app can reach its database and no page renders have been abandoned, 503 otherwise. The
  same probe runs inside the container as podman's health check, so a failing check restarts it; see
  [resource limits and restarts](#resource-limits-and-restarts).
- **Container health:** `ssh deploy@<server-ip> podman healthcheck run uk.osric.asp` forces a check,
  and `podman inspect uk.osric.asp --format '{{.State.Health.Status}}'` shows the current one.
- **Logs:** `ssh deploy@<server-ip> journalctl --user -u uk.osric.asp.service -f`
- **Status:** `ssh deploy@<server-ip> systemctl --user status uk.osric.asp.service`
- **Edge:** `ssh deploy@<server-ip> sudo systemctl status caddy`
- **Rollback:** the quadlet's `Image=` already names the commit the host is running, so going back is
  editing that line to the commit you want and restarting: `podman pull` the
  `registry.osric.uk/uk.osric.asp:<sha>` tag on the host, rewrite `~/.config/containers/systemd/uk.osric.asp.container`,
  `systemctl --user daemon-reload`, `systemctl --user restart uk.osric.asp.service`. Checking out
  the earlier commit and running `make deploy` does the same thing through the gate. A previous image
  may not support the migrated schema; restore its matching backup with the app stopped when
  necessary.

## Memory and swap

The VM has 8 GB of RAM, which is enough for the app, Caddy, Prometheus, podman and the kernel to
share, so the 4 GiB swap file at `/swapfile` is a cushion rather than part of the memory budget
(DEC-87). The arrangement is still deliberately lopsided:

- **The app cannot use it.** The quadlet passes `--memory-swap=2g` beside `--memory=2g`, which sets
  that container's `memory.swap.max` to zero. A page that allocates without bound still meets the OOM
  killer, and the health check still restarts the container in about 35 s; the failure stays fast and
  deterministic instead of becoming minutes of thrash that outlast the probe.
- **Everything else can.** Caddy, Prometheus, podman and the kernel sit outside that cgroup, and the
  swap is what stops a burst — an image pull beside an app restart, an `unattended-upgrades` run —
  from ending in a kill of whichever process scored highest. Paging out an idle proxy costs nothing
  measurable; killing it costs a restart, and for the app, every request in flight.
- **Swappiness is left alone.** With the app excluded, the pages that move are the idle ones, which
  is exactly right. Lowering `vm.swappiness` would make the kernel drop page cache instead and reach
  the OOM killer sooner, not later.
- **Swap is not capacity.** If `podman stats` shows the app's working set at its 2 GB limit, the
  answer is a bigger VM — or a higher limit with the memory behind it — never more swap.
- **The swap file is 5% of the disk.** The 80 GB is shared with the image store, the previous image
  tag kept for rollback, `storage.db` and Prometheus's 4 GB retention. Check `df -h /` before raising
  any of them.

## Resource limits and restarts

The app container is the only thing that can stop a page. .NET cannot interrupt arbitrary C#, so when
a page loops forever the request answers `504` and the thread is lost; the app stops reporting healthy
rather than pretending otherwise. Podman then does the killing.

- **Limits.** `--memory=2g` means a page that allocates without bound gets the OOM killer, and
  `--memory-swap=2g` keeps the container from paging instead of dying ([memory and
  swap](#memory-and-swap)). `--pids-limit=512` stops a thread or process storm taking the host with
  it, and `--cpus=3` leaves a whole one of the four vCPUs for the host-side Caddy and for the probe.
  `StopTimeout=35` is longer than the host's 30 s graceful-shutdown budget, so a normal stop finishes
  cleanly and only a stuck page is `SIGKILL`ed.
- **Probe.** `HealthCmd` runs
  `wget -q -O /dev/null -T 3 http://127.0.0.1:8080/heath` every 10 s; three consecutive failures make
  the container unhealthy, and `HealthOnFailure=restart` restarts it. Detection plus restart is about
  35 s, which is the cost of the design: nothing else can interrupt a page.
- **`/heath` reports two things.** Storage reachability, and how many page renders have been
  abandoned. Two abandoned renders at once is the limit; a page that hangs deterministically therefore
  costs one restart per attempt, not a permanently wedged site.
- **The start limit still applies.** `StartLimitBurst=3` in 60 s means a page that wedges on every
  request will restart three times and leave the unit failed — the intended "stop crash-looping"
  behaviour, but it needs a fix and `systemctl --user reset-failed uk.osric.asp.service` rather than
  waiting. `journalctl --user -u uk.osric.asp.service` names the page path in the warning line.
- **Tuning.** `podman stats` shows the real working set; the memory limit is the knob to raise first if
  the site is OOM-killed under load rather than because of a page. There is room on an 8 GB host, but
  raising it without leaving memory for the page cache only moves the kill from the container to the
  kernel's choice of victim.

## Storage and migrations

Source storage uses EF Core with SQLite. The local default is `ConnectionStrings:Storage` set to
`Data Source=data/storage.db`; the app quadlet overrides it with
`ConnectionStrings__Storage='Data Source=/data/storage.db'`. Route-to-script lookup, the compiled
image of each page and editor persistence all read and write this database; nothing else in the
deployment depends on it.

Each page row carries the assembly emitted when it was last saved, so the database grows by roughly
the size of each page's assembly (tens of kilobytes per page) on top of its source. A release that
changes `uk.osric.asp.Sdk` or `uk.osric.asp.Core` changes the runtime id stored beside every image,
so each page is compiled again on its next request: the first visit after such a deploy is slower,
once, and the row is rewritten in place. There is no startup warm-up, so a page nobody visits is
not recompiled until someone asks for it.

The rootless named volume `uk.osric.asp-storage` is mounted read-write at `/data`. Podman creates it
on first use and reuses it when the container or image is replaced. It belongs to the `deploy` user's
Podman storage, not root's. Removing the volume (including pruning unused volumes) or deleting the
host's storage destroys the data; container replacement is not a backup strategy.

The database also holds the local accounts the editor signs in with: a normalised user name, a
PBKDF2 password hash and a revocation stamp. Treat a backup of `storage.db` as a credential store.
There is no registration page and no user management in the site; accounts are created and changed
only by the `users` command.

Every accepted page save creates a retained revision; source and revision identity are kept
indefinitely unless an editor explicitly scrubs a path. Deleting a page only tombstones its current
head, and startup seeding will not restore a tombstoned `/site.css` or `/rocket.svg`. Saving at that
path recreates it in the same history. Runtime-change cleanup of obsolete compiled images and
symbols is deferred; source history remains. History browsing and restore are still future work.

Scrub removes the live head, if present, and every revision at the path, then requests SQLite
secure-delete and WAL checkpoint cleanup. It is best effort, not a guarantee of forensic erasure.
It does not revoke already-running page code or erase browser/CDN copies, exported archives,
database backups or filesystem snapshots. Rotate exposed secrets and clean those copies separately;
keep backups outside the storage volume and treat them as credential-bearing data.

The second rootless volume, `uk.osric.asp-keys`, is mounted at
`/home/app/.aspnet/DataProtection-Keys`. It holds the data-protection key ring used by the auth,
antiforgery and session cookies. Without it, every restart would invalidate the long-term login and
any form that was already open.

## Local accounts

Create the first account before using the editor. The command runs the same binary and migrations as
the site, and prompts for the password with echo off (or reads one line from standard input when
`--password-stdin` is passed):

```bash
make user-add ACCOUNT=osric
make user-passwd ACCOUNT=osric       # also revokes every existing login for that account
make user-list
make user-remove ACCOUNT=osric
```

On the server, run the same command in the running container; `make` reaches it over SSH with
`podman exec`, so it uses the deployment's own storage volume and connection string:

```bash
make remote-user-add HOST=deploy@<server-ip> ACCOUNT=osric
make remote-user-passwd HOST=deploy@<server-ip> ACCOUNT=osric
make remote-user-list HOST=deploy@<server-ip>
make remote-user-remove HOST=deploy@<server-ip> ACCOUNT=osric
```

`remote-user-add` and `remote-user-passwd` need a terminal at both ends, because the password is
read a key at a time with echo off. The container must be running; if the unit has stopped, use a
one-off container with the same storage volume instead:

```bash
podman run --rm -it \
  -v uk.osric.asp-storage:/data \
  -e 'ConnectionStrings__Storage=Data Source=/data/storage.db' \
  registry.osric.uk/uk.osric.asp:latest users add osric
```

The site starts with no accounts. Until one is created, `/edit` redirects to `/login` and no
credentials work.

Passwords are hashed with PBKDF2-HMAC-SHA256 at 600,000 iterations. There is no password recovery
flow: use `users passwd` from the host. That command rotates the security stamp, so it also signs out
every browser that is already logged in.

The mount's `:U` option recursively changes the volume's ownership to the host UID/GID mapped from
the container's `User=1654:1654`. This lets the unprivileged app create the database and SQLite
journal/WAL files without relaxing permissions. It modifies ownership on the host and may slow
startup on a volume with many files. Keep this volume dedicated to the app; do not substitute a
shared host directory without accepting those ownership changes. See Podman's
[volume ownership documentation](https://docs.podman.io/en/latest/markdown/podman-run.1.html#volume-v).

The app applies pending migrations asynchronously at startup, before serving requests. **Take a
verified backup before starting a new release with migrations**, including unattended updates.
Review generated migrations before deployment; startup migration does not make a backup for you.
In particular, take and verify an off-volume backup before deploying the retained-revisions
migration.

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
loopback and Caddy uses `Network=host` instead of a shared bridge. The app and Prometheus are both
rootless, so they *can* share `uk.osric.asp.network`; container names on that network resolve through
`aardvark-dns`, which `cloud-init.yaml` installs.

## Metrics and the status page

Prometheus scrapes `app:8080/metrics` every 15 s over the shared network and keeps 30 days or 4 GB,
whichever comes first; the container is capped at 512 MB of RAM so runaway cardinality fails a scrape
instead of the host. `/status` is owner-only and queries Prometheus by name, so neither service is
reachable from the internet.

- **Two gates on `/metrics`.** The app publishes on the loopback only (the control DEC-61 describes),
  and Caddy answers `404` for the path on the public origin. Keep both.
- **Prometheus is never proxied.** It has no authentication; reach it with
  `ssh -L 9090:127.0.0.1:9090 deploy@<server-ip>` and open `http://localhost:9090`.
- **No data is a banner, not a failure.** A stopped Prometheus, a slow query, or an unreadable reply
  makes `/status` say so and still render; only a bug in the page itself is a `500`.
- **Retention is a guess.** 30 days and 4 GB are easy to change in `prometheus.container`; check
  `df -h /` before raising them, because images, page assemblies, the database and the swap file
  share the disk.
- **Page storage gauges have distinct meanings.** `page.storage.payload.size` counts logical UTF-8
  bytes in revision text and image/symbol blobs, excluding indexes and SQLite overhead. The bundled
  SQLite has no `dbstat`, so this is not per-table allocated size. `page.storage.database.size` is
  `page_count * page_size` for the whole database, including indexes and freelist pages but excluding
  the WAL. `page.storage.volume.capacity` is the containing filesystem's total capacity. Samples run
  at startup, about every 60 seconds and after a scrub; scrape callbacks use cached values, and
  failed samples retain the last successful values. Review retention manually when logical payload
  exceeds half the volume capacity; revisions are not pruned automatically.
- **Existing hosts need one package.** `aardvark-dns` became part of `cloud-init.yaml` with this
  work, so a VM created earlier has no container DNS: `/status` says Prometheus "did not answer" and
  the scrape target is down. Install it once, then restart the units:
  `sudo apt-get install -y aardvark-dns`, `systemctl --user restart uk.osric.asp.service
  prometheus.service`. `make deploy` refuses to run on a host without it and waits until Prometheus
  can reach `app:8080` before reporting success.

## Proxy trust and forwarded headers

Caddy terminates TLS, so without help the app sees a plain-HTTP request arriving from the loopback
proxy: `Request.IsHttps` is false and `UseHsts` emits nothing. It therefore calls `UseForwardedHeaders`
for `X-Forwarded-Proto`, `X-Forwarded-Host` and `X-Forwarded-For`, which Caddy sets by default, and
trusts those headers from any peer. Caddy replaces these headers rather than passing a client's own
values through, and the middleware reads the rightmost entry, so none of the three can be chosen by
the request.

That is safe only because of the topology. The quadlet publishes the port on `127.0.0.1` alone, so
nothing off the machine can reach the app, and the only client that can send those headers is the local
Caddy. **The loopback-only publish is the control:** do not widen `PublishPort`, and do not place
another proxy in front that rewrites the headers without revisiting this decision.

What it changes:

- HSTS works: `UseHsts` only sends `Strict-Transport-Security` for requests it believes are HTTPS.
- `Request.Scheme`, `Request.Host` and `Request.IsHttps` describe the browser's request, so cookies,
  absolute URLs and redirects derived from them are correct.
- `Connection.RemoteIpAddress` is the browser's address, which is what the sign-in throttle counts
  against, so one client spending its attempts cannot deny sign-in to anyone else.
- Caddy redirects HTTP to HTTPS itself; the app deliberately has no `UseHttpsRedirection`, which behind
  a proxy is a redirect loop waiting to happen.

If the app ever runs somewhere else — another proxy, a load balancer, or a directly published port —
replace the cleared `KnownProxies`/`KnownIPNetworks` with that proxy's address instead of trusting
every peer.

## Image layout and size

Every image goes through `registry.osric.uk`, so the layout is chosen for layers:

| base | app layer per deploy | image |
| --- | --- | --- |
| `mcr.microsoft.com/dotnet/aspnet:10.0-alpine-extra` | 177 kB (framework-dependent) | 160 MB |

The base is a manifest list, and `--platform linux/arm64` selects the arm64 variant that the CAX21
needs; the same digest also carries amd64, so a build on an x86 machine uses the same pin. The
~160 MB .NET runtime lives in the base image's layers, which are pulled once and cached by both
the registry and the host. Each release therefore pushes and pulls only the app layer. The `-extra`
flavour costs about 38 MB over plain alpine and is chosen for its `/usr/share/zoneinfo`: the status
page labels its time axis in Europe/London, and `TimeZoneInfo` throws for a zone the image does not
know. Building it as a self-contained single file instead would make the image smaller (64 MB) but
would move ~52 MB on every release, which is the wrong trade when a registry is available.

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

The first push uploads the base layers (~160 MB); later pushes send only the app layer. Rootless
images live in `~deploy/.local/share/containers`, so `podman images` as root will not show them.

For unattended updates, add `AutoUpdate=registry` to the app quadlet and run
`systemctl --user enable --now podman-auto-update.timer` on the server.

## Notes

- The image is built for `linux-musl-arm64`, matching the CAX21 host. On an x86 machine use
  `make deploy CONTAINER_RID=linux-musl-x64 CONTAINER_PLATFORM=linux/amd64`; the two must agree.
- The app container runs as uid 1654 with all capabilities dropped and `no-new-privileges`; only
  Caddy publishes ports that the outside world can reach.
- Data-protection keys are persistent in `uk.osric.asp-keys`. Deleting that volume signs every
  account out and invalidates in-flight forms; it is also the bluntest way to revoke a login if the
  CLI is unavailable.
