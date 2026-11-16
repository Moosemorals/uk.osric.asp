<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Off-site backups pull themselves

| | |
| --- | --- |
| Status | Proposed and built 2026-11-16 — waits for the owner to merge. Nothing is merged, and the names below are taken by the merge |
| Size | Small — one snapshot script, one pull script, two user units, one package in cloud-init, a few lines in `deploy`, and the operations section that says how to restore |
| Area | deploy (host) |
| Spike | None. Every path and command below was read off `trunk` or run on 2026-11-16: the volume's location from `podman volume inspect`, the online copy from `sqlite3`, the ownership the restore needs from `podman unshare install`, and the restricted key's limits from `rrsync` itself |
| Backlog | None. This is the backup half of `deploy-the-firewall-and-the-backups`, which is edited in place; [OPS-05](../jobs/OPS-05.md) stays the notification work and is not this |
| Decisions | `deploy-the-firewall-and-the-backups` is revised: its "no automatic backups" sentence stops being true. Proposed at the merge: **deploy-backups-are-pulled** |

## The short version

The site has no automatic backup. The volume can be lost by a `podman volume prune`, a dead disk, or a
mistake, and the only copy is the one the owner takes by hand before a deploy. This change gives it a
nightly snapshot and an off-site copy:

- **The host snapshots, nothing else.** `~/bin/backup-snapshot.sh`, run by a user timer at 03:15, takes
  SQLite's online backup of `storage.db` inside `podman unshare`, checks it, and leaves it in
  `~/backups` with a month of daily copies. The site keeps serving throughout.
- **The backup host pulls.** `deploy/pull-backups.sh`, run on the backup host, copies the newest
  snapshot into a dated directory, verifies it, refuses one that is more than a day old, and prunes
  what is older than 90 days.
- **The direction is the decision.** The internet-facing box holds no credential to the machine that
  keeps its history, and the history cannot be deleted by a bad day on the host.

## Goals

1. Losing the volume costs a day, not the site: pages, revisions, state, sessions, accounts and the
   assistant's memory come back from a file the host does not hold.
2. The copy is taken without stopping the app, and is a transaction rather than a file copy — the WAL
   problem [operations](../../deploy/operations.md#storage-and-migrations) already refuses to ignore.
3. A backup that has quietly stopped looks like a failure, not like success. The pull is where that is
   detected, because that is the machine that is not the one being backed up.
4. Restoring is written down and needs no invention: stop, copy into the volume as the container's
   uid, start, wait for `/health`.
5. Nothing new to learn: the units are shipped from `deploy/` and installed by `make deploy`, exactly
   as the quadlets are (`deploy-quadlets-are-the-source-of-truth`).

## Non-goals

- **A backup of Prometheus.** Thirty days of metrics are disposable; a lost volume costs a chart, not a
  fact. Caddy's volumes are the same: it reissues a certificate on first request.
- **Outbound alerting.** A failure is still found by reading a log, which is
  `deploy-no-outbound-alerting`'s question and [OPS-05](../jobs/OPS-05.md)'s work. The pull fails
  loudly in its own journal and leaves no dated directory behind.
- **Incremental or encrypted archives.** The database is tens of megabytes; a compressed full copy a
  day is cheaper than the code that would avoid it. Encryption at rest is the backup host's disk.
- **A restore button.** `/admin/import` carries pages, not users, sessions or history; the file is the
  backup ([IDEA-020](../jobs/IDEA-020.md)).
- **A VPN.** The pull needs nothing but the SSH the host already accepts. If the host is ever put on
  the VPN the pull can run over it, but the direction does not change.

## What exists today

| Piece | Where | What it does |
| --- | --- | --- |
| The database | `uk.osric.asp-storage` → `/data/storage.db` | Every page, revision, page state, session, account and assistant memory; a credential store, because it carries password hashes |
| The key ring | `uk.osric.asp-keys` → `/home/app/.aspnet/DataProtection-Keys` | Signing keys for the auth, antiforgery and session cookies; losing it signs everyone out and invalidates open forms, and nothing else |
| Host-only configuration | `~/.config/uk.osric.asp/assistant.env`, `appsettings.Production.json` | The provider key and the content-type override; neither is in the repository |
| The backup story | [operations](../../deploy/operations.md#backups) | Says to stop the unit or use `podman unshare`, and that backups must be off-host. Nothing runs it |
| The manual step | [deploy/README.md](../../deploy/README.md#day-to-day) | "back up storage, then `make deploy`" — the owner's habit, not a mechanism |
| Packages on the host | [cloud-init](../../deploy/cloud-init.yaml) | `sqlite3` is there for this; `rsync` is not, and the pull needs it at both ends |

## Design

### The snapshot, on the host

`deploy/backup-snapshot.sh` is installed as `~/bin/backup-snapshot.sh` and run as `deploy` by
`uk.osric.asp-backup.timer` (`OnCalendar=*-*-* 03:15:00`, `Persistent=true`, a random delay of up to ten
minutes). It is a `oneshot` service, so `systemctl --user start uk.osric.asp-backup.service` takes one
on demand — which is what the pre-deploy habit becomes.

It does four things, then tidies one scratch case:

1. Copies `assistant.env` and `appsettings.Production.json` into `~/backups/conf` — the only host files
   the repository does not carry.
2. Asks podman for the volume's mountpoint — which is the directory mounted at `/data`, so
   `storage.db` sits directly in it — then runs `sqlite3 <mountpoint>/storage.db ".backup
   <tmp>"` through `podman unshare`. The files are owned by a mapped subordinate id, so the copy has to
   be made in the same user namespace; `.backup` is SQLite's online API and is safe while the app
   writes. `-readonly` is deliberately not passed: a container killed mid-write leaves a hot journal
   that a read-only open cannot roll back, and recovery is what the copy needs.
3. Runs `PRAGMA integrity_check` on the copy, so a bad copy never replaces yesterday's good one.
4. Gzips it to `storage.<dd>.db.gz` — a 31-entry ring keyed by day of month, not a queue of the last
   31 runs, so a month is kept, a missed day stays a hole until that date returns, and the same date
   next month overwrites the slot — then hard-links that file to `storage.db.gz` through a scratch name
   and an atomic `mv`. `storage.db.gz` is the name the pull takes: a hard link rather than a symlink
   because the pull's restricted `rrsync` refuses to follow one, and the scratch name means the pull
   never sees a partial or missing file.

`umask 077` plus an explicit `chmod 600` on the archive, because SQLite creates its output `0644`
regardless of the umask and this copy is a credential store.

Nothing else in `~/backups` is ever deleted. The day names replace themselves, and the pull only reads
— so the directory is bounded at 31 archives, `storage.db.gz`, `conf/` and one transient name. The one
exception is a run that is SIGKILLed: it never reaches its trap, so it leaves one scratch file behind,
and the next run deletes scratch names older than a day.

### The pull, on the backup host

`deploy/pull-backups.sh` is kept here as the source of truth and copied to the backup host, which has no
checkout. It takes the source and destination as arguments so it can be run against a local directory
when it is changed, and it uses one key.

```text
pull-backups.sh [deploy@moosemorals.com:backups] [$HOME/backups/uk.osric.asp]
```

1. `rsync -a` the `storage.db.gz` hard link and `conf/` into a staging directory `.YYYY-MM-DD.partial`.
2. Refuse a snapshot whose mtime is 26 hours old or more: a failed snapshot on the host leaves
   yesterday's file in place, and without this check it would be filed as today's.
3. `gzip -t`, `PRAGMA integrity_check`, and a check that `PageHeads`, `Users` and
   `__EFMigrationsHistory` are all present — a file that is a valid SQLite database but not this
   database is refused.
4. Only then rename the staging directory to `YYYY-MM-DD`, replacing an earlier pull from the same day,
   and prune directories older than `KEEP_DAYS` (90 by default).

Everything it writes stays under `~/backups/<site>/`, with the script itself at `~/bin/pull-backups.sh`
— the home directory rather than a project folder, because a checkout is disposable (`make clean`, a
re-clone, a removed worktree) and this copy is not, and rather than `/srv`, so the cron run needs no
`sudo` and owns the key and the directory it writes. The directory is the project's name, `uk.osric.asp`,
the same one the image, the volumes and the units use; another site gets a sibling. Each site's prune
only walks its own directory.

The key is `~/.ssh/id_backup_uk_osric_asp` on the backup host, and its public half goes in
`deploy@…:~/.ssh/authorized_keys` with `restrict,command="/usr/bin/rrsync -ro /home/deploy/backups"`. That
key can read that directory and do nothing else. There is no `from=` clause: a domestic connection's
address changes, and the forced command is the control that matters.

It carries no passphrase, because it is used by an unattended run and an agent loaded at boot would be
one more thing to keep working. What replaces the passphrase is where the file lives and what it can
do: `0600` on the machine that is not the public one, a forced command that only reads one directory,
and `BatchMode=yes` with `IdentitiesOnly=yes` so a cron run fails rather than waits on a prompt. The
key is not backed up — regenerating it costs one `authorized_keys` line.

The schedule is the backup host's own — cron there is fine, because it is not this host and does not
need the user namespace — one run a day, after 03:15. It needs `rsync`, `sqlite3` and `gzip`; the
site's host needs `rsync` as the far half of the transfer, and `cloud-init.yaml` now installs it.

### Why pull rather than push

- **The credential sits on the machine with nothing to lose.** The site is the public box. A key that
  can write to the backup host is a key an attacker on the site would want; a key that reads one
  directory is worth much less.
- **The destination owns retention.** A `--delete` mirror is only ever as good as the source's current
  directory, so one bad day erases the history meant to survive it. Dated directories on the backup
  host make a bad copy additive rather than destructive.
- **Verification happens off the host.** `integrity_check` and the age check run on the machine that
  is not the one being backed up, which is the only place a claim about the host is worth anything.

## Failure handling

| What fails | What it looks like |
| --- | --- |
| The snapshot script | The oneshot unit fails, `journalctl --user -u uk.osric.asp-backup` has the line, and the backup host's pull refuses the stale file and exits non-zero — so a silent stop becomes a failed pull within a day |
| The volume | The script says the volume is not there and exits non-zero; `storage.db.gz` still names a good copy |
| The host is down | The pull fails to connect and stores nothing; nothing is pruned |
| A corrupt copy | `integrity_check` fails in the snapshot, or in the pull; either way yesterday's dated directory is untouched |
| The backup host is down | The host keeps its month of daily copies; when the pull resumes it takes the newest, and the gap is visible in the dates |

## Restore

On the host, as `deploy`, after copying the newest good `storage.db.gz` across:

```bash
systemctl --user stop uk.osric.asp.service
gunzip -c /tmp/storage.db.gz > /tmp/storage-restore.db
mountpoint=$(podman volume inspect --format '{{.Mountpoint}}' uk.osric.asp-storage)
podman unshare rm -f "$mountpoint/storage.db-wal" "$mountpoint/storage.db-shm"
podman unshare install -o 1654 -g 1654 -m 0640 /tmp/storage-restore.db "$mountpoint/storage.db"
rm /tmp/storage-restore.db
systemctl --user start uk.osric.asp.service
until curl -fsS -o /dev/null http://127.0.0.1:8080/health; do sleep 2; done
```

`1654` is the container's uid, and the volume is created with `:U`, so the file has to arrive with that
ownership inside the namespace; `podman unshare install` is what sets it. The stale `-wal`/`-shm` are
removed because replaying a newer log over an older database is how a restore corrupts itself.
Restore only onto an image whose schema the file matches: migrations run forward at startup and a file
from a newer release is not one an older image can open.

## What this does not cover

- **The key ring volume.** Deliberately not in the snapshot: it is a credential of its own, and a
  restore without it only signs everyone out. Copying it is a separate decision, and would be
  `podman unshare tar` of the volume with the same ownership care.
- **The image and the configuration.** The image is in the registry by commit and the quadlets and
  Caddyfile are in `deploy/`, so a rebuild is `make deploy`; the two host-only files are in the
  snapshot.
- **Filesystem corruption under the volume.** Podman's storage is inside `/home/deploy`; the snapshot
  reads through it, so this copy is only as good as that filesystem. The off-site copy is what covers
  it.

## Files

| File | Change |
| --- | --- |
| `deploy/backup-snapshot.sh` | New: the online copy, its check and its names |
| `deploy/uk.osric.asp-backup.service`, `.timer` | New: the oneshot and its daily clock |
| `deploy/pull-backups.sh` | New: the backup host's pull, verification and prune |
| `deploy/cloud-init.yaml` | `rsync`, because the pull runs `rsync --server` on this host |
| `Makefile` | `deploy` installs the script and the two units, enables the timer, and refuses a host without `rsync` as it already does without `aardvark-dns` |
| `deploy/operations.md` | `#backups` becomes the whole story: what, when, where, how it is verified, how to restore, what is not covered |
| `deploy/README.md` | The first-deploy step and the day-to-day backup habit point at the nightly snapshot and its on-demand run |

## Validation

There is no test harness for shell in this repository and adding one would need podman in the gate, so
the scripts are checked where they run:

- `bash -n` on both scripts.
- `backup-snapshot.sh` against a throwaway podman volume named `uk.osric.asp-storage`, with a database
  built by `sqlite3`, and the day-of-month file, the `storage.db.gz` hard link, the `0600` mode and the
  restored rows checked afterwards.
- `pull-backups.sh` with a local directory as its source: the dated directory, the `conf/` copy, the
  stale-file refusal (an mtime set 27 hours back), a corrupt file refused, and the prune keeping a
  directory inside its window and removing one outside it.
- `make decisions` (no new citations) and `make license-check` (the new files carry their SPDX tags).

## Risks

| Risk | Answer |
| --- | --- |
| The timer never runs because lingering is off | Lingering is enabled by cloud-init and already relied on by `podman-auto-update.timer`; a stop shows up as a failed pull within a day |
| The pull host is down for a week | The host keeps a month locally; the dates in the backup directory show the gap |
| A single day's copy is corrupt | The pull keeps every earlier day; a restore picks the newest good file |
| The database grows past the disk | A month of copies at tens of megabytes is nothing against 80 GB; the metrics section already asks for `df -h` before raising retention |
| The key leaks from the backup host | It can only read `~/backups` on this host through `rrsync`; the snapshot it reads is already credential-bearing, which is why it is `0600` |
| Nobody reads the failed pull | That is [OPS-05](../jobs/OPS-05.md), and it is unchanged by this plan |

## Delivered when

The timer takes a snapshot nightly and `systemctl --user start uk.osric.asp-backup.service` takes one
on demand; the file passes `integrity_check` and carries `0600`; a pull from the backup host produces
`YYYY-MM-DD/storage.db.gz` and `conf/`, and refuses a stale or corrupt file; the restore steps in
[operations](../../deploy/operations.md#backups) have been run once against a throwaway volume; and
`make decisions` and `make license-check` are green.

## What changed while building

- The volume's mountpoint is the directory mounted at `/data`, so `storage.db` sits directly in it and
  `_data` must not be appended. The first deploy found this: the installed snapshot service stopped with
  "volume uk.osric.asp-storage has no mountpoint". The script and the restore steps now use the
  mountpoint as the directory.
- The restricted key changes what the site may serve: `rrsync -ro` refuses `--copy-links`, so the
  snapshot leaves `storage.db.gz` as a hard link to the day's file instead of a `latest` symlink. The
  pull was then run through a real `rrsync -ro` forced command, with a stand-in `ssh`, rather than only
  against a local directory.
- The pull checks `PageHeads`, `Users` and `__EFMigrationsHistory`, not a `Pages` table: `Pages` belongs
  to the export archive, and the store has no table by that name.
- Validation on 2026-11-16: both scripts pass `bash -n`; a snapshot against a throwaway volume wrote a
  `0600` archive whose copy answered `PRAGMA integrity_check` and held the expected rows, and a second
  run replaced it and relinked `storage.db.gz`; a 27-hour-old file, a corrupt gzip and a valid database
  with the wrong schema were each refused with a non-zero exit and no dated directory written; of two
  dated directories, the 100-day one was pruned and the 5-day one kept; and the restore below was run
  against that volume, putting the snapshot back with `1654:1654` inside the namespace and losing the
  row that had been added after the snapshot.

## The names this plan intends

- **deploy-backups-are-pulled** — the backup host reaches in and copies; the site's host never holds a
  credential to the machine that keeps its history, and the history cannot be erased by a bad day on
  the host.
