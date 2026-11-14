<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Running it

The host's limits, storage, backups, metrics and proxy trust: what to check when the site is up and
something is wrong. [Deployment](README.md) is how a release gets there in the first place.

## Content types

A stored page's default response type comes from the last extension in its key:
`Pages:ContentTypes:Extensions` maps an extension to a media type and `Pages:ContentTypes:Fallback`
covers every other key (`text/html; charset=utf-8` by default). The shipped map lives in the image's
`appsettings.json`; `ctx.SetContentType` overrides it.

A host that wants a different map edits `~/.config/uk.osric.asp/appsettings.Production.json`, which
`make deploy` seeds once with `{}` and the quadlet mounts read-only over
`/app/appsettings.Production.json`. Providers merge key by key, so the file lists only what it
changes — a new extension, a replacement row, or the fallback — and mapping a row to the fallback
retracts it. Restart the service to apply an edit; that is not a redeploy:

```bash
ssh deploy@<server-ip> systemctl --user restart uk.osric.asp.service
```

A malformed row is fatal: the container does not come up and the journal names the offending key
(`journalctl --user -u uk.osric.asp.service`). Check a response with `curl -sI` for both headers:

```bash
curl -sI https://moosemorals.com/site.css | grep -iE '^(content-type|cache-control):'
```

Every rendered page is `Cache-Control: no-cache` unless the page sets its own, and an authenticated
response is `no-store` whatever the page set. The shipped map names `.js`, `.mjs`, `.css`, `.json`,
`.svg` and `.txt`; an `.svg` page is the owner's own content, served with the shell's policy and
refused script unless the page asks for it (DEC-197). The mount is read-only on purpose: no page,
editor or assistant can change the map.

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

The app container is the only thing that can stop a page: .NET cannot interrupt arbitrary C#, so a
page that loops forever is answered `504` and its thread is lost, and the app stops reporting healthy
rather than pretending otherwise. [Pages](../docs/pages.md#health-and-restarts) has the reasoning;
this is what podman does about it.

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

## Configuration

Every policy value the site needs is required: there is no fallback in code, so a host that is missing
one does not start. The image ships a complete `appsettings.json`, and a deployment overrides only what
it changes in `~/.config/uk.osric.asp/appsettings.Production.json` or an environment variable
(`Assistant__ApiKey`, `ConnectionStrings__Storage`). Providers merge key by key, so an override file
does not have to repeat the rest.

A value that is missing, blank, malformed or out of range stops the process before it serves, with
one paragraph naming each bad key, the range it accepts and the value this repository ships:

```
The site cannot start because its configuration is incomplete or invalid:
  Pages:Render:TimeoutSeconds is 0, which is outside the accepted range of 0.05 to 60. Set it to a number from 0.05 to 60; 5 is recommended.
Set these values in appsettings.json, appsettings.<Environment>.json or an environment variable, then start the site again.
```

The app exits non-zero, so `systemctl --user status uk.osric.asp` shows the restart limit rather than
a serving site, and the same text is on the journal. Fix the setting, restart, and the site comes up.
The list of keys is in the shipped `appsettings.json`; each subsystem's document explains what its
section is for.

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

Every save creates a retained revision, deleting a page only tombstones its head, and scrub removes
one path's head and revisions: [storage](../docs/storage.md#source-storage) has the rules. Startup
seeding will not restore a tombstoned `/site.css` or `/rocket.svg`, so saving at that path recreates
it in the same history. Runtime-change cleanup of obsolete images and symbols is deferred. The
editor's history view shows a retained revision and restores it as a new save, after a confirmation
that shows the change; a seeded row installs its bundled source the same way
([the plan](../docs/plans/archive/revision-restore.md)).

Scrubbing is best effort, not forensic erasure: it does not reach running code, browser or CDN
copies, archives, backups or filesystem snapshots. Keep backups outside the storage volume and treat
them as credential-bearing data.

The second rootless volume, `uk.osric.asp-keys`, is mounted at
`/home/app/.aspnet/DataProtection-Keys`. It holds the data-protection key ring used by the auth,
antiforgery and session cookies. Without it, every restart would invalidate the long-term login and
any form that was already open.

### One-off: the squashed migration

**Done on the CAX21 on 2026-11-07.** This is kept as the record of what the DEC-122 deploy cost, and
as the procedure if a second host ever holds a pre-squash volume; a fresh host needs none of it.

The migration history was squashed into a single `InitialCreate` for DEC-122, which renamed the
assistant's EF entities. `__EFMigrationsHistory` in an existing `uk.osric.asp-storage` volume does not
know about `InitialCreate`, so startup finds tables it did not create and the app fails. It costs two
volumes and one account, and the order matters: when the release has already been deployed, the app is
crash-looping against the old schema and both volumes are still held by running containers. The podman
commands run on the host as `deploy`; the last line runs from the repository.

```bash
# Stopping both units releases both volumes; the app may be in the start-limit failed state already.
systemctl --user stop uk.osric.asp.service prometheus.service
systemctl --user reset-failed uk.osric.asp.service prometheus.service
# If podman still reports a volume in use, remove the stopped containers and retry the next line.
podman volume rm uk.osric.asp-storage prometheus-data

systemctl --user start uk.osric.asp.service prometheus.service
# The app applies InitialCreate before it serves, so wait for health before creating the account.
until curl -fsS -o /dev/null http://127.0.0.1:8080/heath; do sleep 2; done
make remote-user-add HOST=deploy@<server-ip> ACCOUNT=osric
```

Only these two volumes go. `uk.osric.asp-keys` holds the data-protection keys and is not disposable;
`caddy-data` and `caddy-config` are untouched by the schema. Once the new image is already on the host
a redeploy is unnecessary — starting the units recreates the schema from `InitialCreate`, and
`make deploy` would only restart the same two units after rebuilding and pushing.

`uk.osric.asp-storage` goes because the schema no longer replays from the old history, and it takes
the owner account with it — hence the `remote-user-add` afterwards. `prometheus-data` goes because
DEC-122 also renamed the page meter to `uk.osric.asp.Pages` and two instruments to
`page.load.duration` and `page.invoke.duration`: with 30 days of retention, the old and new series
would otherwise sit side by side for a month and any panel that sums without filtering by name would
count both. Nothing in either volume is worth keeping.

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
- **One chart can be linked on its own.** `/status/chart?panel=<id>&start=<ISO-8601>&duration=<token>`
  draws one panel for one window, under the same owner policy: a bridge or a bookmark for the owner,
  not a public embed. `duration` is a window token (`1h`, `3h`, `8h`, `1d`, `7d`, `30d`) or a whole
  number of minutes. A malformed window is a `400`, an unknown panel a `404`, and a Prometheus that
  does not answer a `503` with the reason.
- **Restore `/status.css` after deploying this page.** The sheet is a seeded page (DEC-174), so an
  installation that already has one keeps the old rules until the owner restores it from `/edit`.
  Until then the charts render full width, one per row: usable, but not the intended grid. The
  assistant's own seeded `/assistant.css` and `/assistant.js` are the same: a deployment adds the
  continuation card and its script to the image, and an installation keeps the old sheet and script
  until the owner restores them, so a paused run would show no card until then.
- **Retention is a guess.** 30 days and 4 GB are easy to change in `prometheus.container`; check
  `df -h /` before raising them, because images, page assemblies, the database and the swap file
  share the disk.
- **Page storage gauges mean different things.** Payload is logical UTF-8 bytes, database size is
  the whole file and capacity is the volume; [metrics](../docs/metrics.md#instrumentation) has the
  definitions and the sampling rule. Review retention manually when payload exceeds half the volume
  capacity: revisions are not pruned automatically.
- **Container DNS.** `cloud-init.yaml` installs `aardvark-dns`, which resolves `app` and `prometheus`
  by name on the shared network. A host without it shows `/status` saying Prometheus "did not
  answer" and a down scrape target: `sudo apt-get install -y aardvark-dns`, then restart both units.
  `make deploy` refuses to run on such a host.

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
| `mcr.microsoft.com/dotnet/aspnet:11.0-alpine-extra` | 26.7 MiB (framework-dependent) | 190.7 MiB |

The default pin is the arm64 image digest, which `--platform linux/arm64` selects for the CAX21. MCR
does not resolve its manifest-list digest by digest, so an x86 build must set `CONTAINER_BASE_IMAGE`
to the matching amd64 image digest as well as setting `CONTAINER_PLATFORM=linux/amd64` and
`CONTAINER_RID=linux-musl-x64`. The ~164 MiB .NET runtime lives in the base image's layers, which are
pulled once and cached by both the registry and the host. Each release therefore pushes and pulls
only the app layer. The `-extra` flavour costs about 38 MB over plain alpine and is chosen for its
`/usr/share/zoneinfo`: the status page labels its time axis in Europe/London, and `TimeZoneInfo`
throws for a zone the image does not know. Building it as a self-contained single file instead would
make the image smaller (64 MB) but would move ~52 MB on every release, which is the wrong trade when
a registry is available.

Trimming is deliberately off, and the measurements say it should stay off: it cannot be combined
with the shared framework (a framework-dependent `PublishTrimmed` publish is refused with
`NETSDK1102`), so a trimmed release would move 54–58 MB instead of today's 27 MB, and the parts that
would have shrunk are the parts that have to be kept — EF, the migrations assembly and
`System.Private.CoreLib`. [The trimming spike](../docs/spikes/trimmed-host.md) has the numbers, the
failure chain and the one thing it does buy (about 92 MB of RSS).
