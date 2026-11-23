<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Email for moosemorals.com

Status: **proposed, not implemented**. Written 2026-11-06, rewritten 2026-11-23. This is a design and
an implementation plan, not a record of work done. It assumes the host in
[deploy/README.md](../../deploy/README.md) — and it **changes the website**, which grows three service
routes and the means to authenticate them.

There is no mailbox today: no `MX`, one SPF record saying `v=spf1 ~all`, no DMARC. The host is a
Hetzner CAX21 on Debian 13, rootless podman quadlets for the app and Prometheus, one rootful Caddy.

The plan's shape comes from four facts. Mail the host sends *itself* can be delivered locally, so the
first useful phase needs no DNS and no outbound port 25. The machine has its own name, so the bare
domain stays for people. The mail containers run rootless as a user of their own. And there is exactly
one set of accounts: a mailbox is an account is a person, and the website's database is the only place
that says who exists.

The merge names these decisions; the numbers here are only reading order.

## In one page

- **A pod** of Postfix, Dovecot, Rspamd and a small socketmap gateway, beside the Caddy that already
  holds the certificates. One network namespace, so LMTP, the milter, Dovecot's auth socket and the
  host's own submission are `127.0.0.1:<port>`.
- **Rootless, under `vmail`** — a host user of its own, not the website's. One sysctl lets it publish
  25/587/993; a root timer hands it the certificate; an escape lands on a uid that owns mail and
  nothing else.
- **The host's own mail never leaves the host**: `unattended-upgrades` and friends mail `root`, which
  becomes `root@neith.moosemorals.com`, which aliases to the owner's mailbox.
- **One account, one mailbox, one human, several aliases.** The account is the editor's account: one
  password, checked by the same site, so a password change is a change on every mail client too.
- **The mail host reads identity over loopback HTTP**, never through Caddy: three `/internal/mail`
  routes — auth, lookup, settings — refused at the edge.
- **Postfix asks a gateway we write**: socketmap in, HTTP out, no state; delivery never depends on the
  website.
- **Rspamd is the milter, advisory only**: it verifies, signs and tags, and may never reject, greylist
  or discard. No Redis — Bayes learns into two SQLite files, one corpus per account, and ratelimiting,
  greylisting and replies are the modules that cannot run.
- **Tuning per account, filing per address**; **DNS published by the host** from a narrow API key, with
  SPF naming the addresses literally and the PTR set in the same run.

## Goals and non-goals

Goals:

- Deliver the host's own mail to a local mailbox and read it over IMAP, with no dependency outside the
  host except its own name.
- Receive mail for `moosemorals.com`, send as it over authenticated submission, read it over IMAP.
- Publish SPF, DKIM and DMARC; evaluate and report inbound authentication.
- Filter spam without ever losing a message: score, tag, file.
- Several mailboxes from the start — several people, each with an address and aliases — because
  retrofitting an account model is worse than carrying one.
- One place that says who exists: the website's database, with nothing on the host that can disagree.
- Fit beside the website on one machine without becoming the website's problem.

Non-goals:

- POP3, webmail, CalDAV/CardDAV, mailing lists, catch-all, multi-domain hosting.
- High availability, a second mail host, importing mail (there is none).
- A second set of people: mail gets no accounts, no password table, no lockout of its own.
- Redis, and the modules that only run on it: ratelimiting, greylisting, replies, Redis-backed fuzzy
  storage and Bayesian expiry.
- Greylisting, DNSBL rejects, SMTP-time quarantine: global decisions that cannot be per-recipient
  advice.
- The pages that edit accounts, aliases and settings — the next plan; this one builds the rows and the
  routes.
- Any dependency on outbound port 25 before the phase that wants it, and any third-party relay for the
  host's own notifications.

## What already exists

**Host.** Hetzner CAX21 — four Arm64 vCPUs, 8 GB RAM, 80 GB SSD, Debian 13 — podman 5.4.2 with pod
quadlets. Caddy is system-rootful because it binds 80/443; the app and Prometheus are rootless user
units. `ufw` allows 22, 80 and 443. A 4 GiB swap file; the app capped at 2 GB; Prometheus up to 4 GB;
all of it on the one disk with `storage.db` and the container store.

**Zone.** `ns1`/`ns2.mythic-beasts.com`, editable through the Mythic Beasts DNS API:

| Record | Now | Consequence |
| --- | --- | --- |
| `A` / `AAAA` at the apex | the VM's own addresses | — |
| `PTR` for both | `moosemorals.com` | must become `neith.moosemorals.com` |
| `MX` | none | cannot receive mail |
| `TXT` at the apex | `v=spf1 ~all` | soft-fails; exactly one SPF record must remain |
| `_dmarc` | none | no policy |
| `CAA` | `issue "letsencrypt.org"` | Caddy can obtain the mail certificate |

**Missing.** 25, 587 and 993 are closed and nothing listens on them; no mail host name; no mail
volumes; no `vmail` user. Greenfield: no mailbox to migrate, no MX to cut over.

## Decisions

### 1. A pod of containers, not one container with a supervisor

Postfix, Dovecot, Rspamd and the map gateway, in one pod. The hard part of multi-container mail was
never the count of daemons: it was making Postfix find Dovecot's sockets and Rspamd's milter. A pod
makes each of those `127.0.0.1:<port>`.

| | One container, one supervisor | Pod |
| --- | --- | --- |
| Units | one `.container` | one `.pod`, four `.container` files |
| Configuration | one directory | one mounted directory per daemon |
| Upgrades | all at once | independent tags |
| Failure | one bad process can take the container | a healthcheck and restart per daemon |
| Isolation | filter and store share a filesystem | separate filesystems, one network namespace |

A crashed Rspamd must not stop Postfix accepting mail; restarting Dovecot must not drop the SMTP
listener. Three healthchecks say that; a supervisor hides it. If Phase 0 shows the pod misbehaving,
the fallback is one container — a packaging change, because the interfaces stay the same.

### 2. Rootless, under a user of its own

A **user** quadlet like the app, running as **`vmail`** — a host user created for it, never the
account that owns the website. That last part is the cheap win: a compromise of Postfix or Dovecot
lands on a uid that owns the mail volumes and cannot read `storage.db`, the deploy user's `~/.ssh` or
Caddy's store.

Three things were said to need root; none does:

- **Low ports.** Inside a user namespace the container is root and binds 25, 587 and 993; only the
  host-side publish is privileged, and **pasta** makes it once
  `net.ipv4.ip_unprivileged_port_start` is lowered to 25. That is one `sysctl.d` file, and its cost is
  plain: any local user could then bind a privileged port. One human and one service account bound
  it; a second tenant is the moment to revisit.
- **Certificates.** The containers cannot read Caddy's store and should not; a root timer copies the
  leaf and its key into `vmail`'s home, readable by the daemons and nobody else (Decision 11).
- **Secrets.** There is no password file, because credentials live in the site (Decision 7). The DKIM
  key is generated under `vmail` and stays `0640` there.

Costs: the sysctl, a subuid/subgid range for a user that never logs in, `enable-linger vmail`, and
every mail command naming that user. Phase 0 proves the four together; the fallback to system units is
a packaging change.

### 3. Two corridors

They are different projects with different prerequisites, and only the second is hard:

| | Host → local mailbox | Anyone → another host |
| --- | --- | --- |
| Path | loopback listener → queue → LMTP → Maildir | Postfix's `smtp` client → the recipient's MX |
| Needs | the pod, an alias, a Maildir | outbound 25, a PTR, SPF, DKIM, DMARC, a reputation |
| Fails if | the pod is down | any of those is wrong |
| Unblocked by | nothing outside the host | Hetzner enabling outbound 25, or a smarthost |

The local corridor comes first because it answers today's need and exercises everything the public
half shares. The public half's real risk — a new address with no history — is bounded to the last
phase.

### 4. Postfix and Dovecot own SMTP, LMTP and IMAP

Postfix 3.10 is virtual-mailbox-only: no Unix users, no local delivery, no `mydestination`. Dovecot 2.4
owns the store and is the only process that writes it.

| Port | Protocol | Process | Purpose | Public |
| --- | --- | --- | --- | --- |
| 25 | SMTP | Postfix | inbound MX; no AUTH | yes |
| 587 | SMTP + STARTTLS | Postfix | submission; SASL | yes |
| 993 | IMAPS | Dovecot | IMAP | yes |
| 2525 | SMTP | Postfix | the host's own mail; no AUTH, no TLS | host loopback |
| 24 | LMTP | Dovecot | Postfix's delivery | pod |
| 12345 | Dovecot auth | Dovecot | Postfix SASL | pod |
| 11332 | milter | Rspamd | scan and sign | pod |
| 11334 | HTTP | Rspamd | controller, `ssh -L` | host loopback |

Absent: 110, 995, 143, 465, 4190. A client that cannot do 993/587 is a client to revisit, not a reason
to open more ports.

`chroot` is off throughout — the container is the boundary. Port 25 has SASL off; 587 has it on with
`encrypt`. `virtual_transport = lmtp:inet:127.0.0.1:24`, so Postfix is the SMTP server and the LMTP
client, and it never mounts the mail volume. An unknown recipient is rejected at `RCPT TO` rather than
accepted and bounced; `mynetworks` and `reject_unauth_destination` keep 587 from being a relay. The
maps themselves are answers, not files (Decision 8).

### 5. `+` addressing passes the detail through

`osric+anything@moosemorals.com` must reach `osric@moosemorals.com` with no alias line, and the
extension must survive, because it is the most useful thing in the message:

- **`recipient_delimiter = +` in both places, and it is a delivery setting, not an identity.**
  Postfix consults its maps with the extension stripped — so the address is known to exist — and
  delivers the recipient with it intact. Dovecot's LMTP splits it at `smtp_address_detail_parse_temp`
  and delivers to the user before the `+`; **authentication does not split it at all** — `+` appears
  nowhere in `src/auth` — so a plus address is something a sender uses, never a way to log in
  ([spike](../spikes/mail-stack-preflight.md)).
- **`propagate_unmatched_extensions`** stays at its default (`canonical, virtual`), written out because
  it is load-bearing: `shopping+order@` → `osric@` arrives as `osric+order@`.
- **Sieve gets the detail** as the envelope's, and `mail-check` asserts the resolution by delivering,
  not by asking the userdb: `doveadm user` consults the userdb, which never sees a split, so the check
  is a message to `osric+probe@` landing in `osric@`'s mailbox.

If the pinned Dovecot does not strip the detail, the fallback is a Postfix regexp map that does — and
its cost is exactly the detail Sieve would then lose.

### 6. The host submits through a loopback listener

The host's daemons write to `/usr/sbin/sendmail`; Postfix is in a pod; so a small relay agent on the
host hands the message to a listener that trusts it.

- **`msmtp` plus `msmtp-mta`**, Debian packages: `/usr/sbin/sendmail` is msmtp, `/etc/mailname` is
  `neith.moosemorals.com`, `/etc/msmtprc` is `0600 root:root`, points at `[127.0.0.1]:2525` with
  `auth off`/`tls off`, and sets `from alerts@moosemorals.com` so machine mail is identifiable at a
  glance and at a filter. A send-only SMTP client is TLS, DNS, SASL, address parsing and
  dot-stuffing somebody else already got wrong; we own four lines of config.
- **`local_submission` on 2525**, published as `127.0.0.1:2525` only: no SASL, no TLS, `permit_mynetworks`
  with a `mynetworks` of the pod's loopback plus **the host's own address**, and
  `milter_macro_daemon_name=ORIGINATING`.
- **Header qualification gets its own `cleanup`**, because that is where the setting lives: `From: root`
  becomes `From: root@neith.moosemorals.com`.
- **The address the publication arrives from is the host's, and it is measured.** Pasta forwards a
  published port itself and preserves the source, so the container sees the host's own address rather
  than a pod gateway: `mail-deploy` writes that address into `mynetworks` where it writes the rest of
  the host's facts. Omitted, 25 accepts and 2525 rejects, which is loud
  ([spike](../spikes/mail-stack-preflight.md)).

`myhostname = neith.moosemorals.com`, `myorigin = $myhostname`, empty `mydestination`: `root` becomes
the machine's address, matched by an alias, delivered to the owner's mailbox. `neith.moosemorals.com` is
an alias-only domain, so the machine resolves recipients without becoming a place people are addressed.

`msmtp` does not queue: if the pod is down, `sendmail` fails and the message is lost. An accepted trade,
repeated in Risks.

### 7. One account, one mailbox, one human

There is exactly one set of accounts, and it is the website's. A mailbox **is** an account — the one
the editor signs in with — belonging to one person, with one canonical address and any number of
aliases pointed at it. No second table of people, no shared mailbox, no second password. Revoking,
changing or locking an account applies to the site and to mail together, at once.

Two consequences are part of the design rather than accidents:

- **One password.** Changing it on the site means changing it in every mail client; the next sign-in on
  each device fails until it does. Sessions already open keep working — Dovecot authenticated them
  once. Per-device app passwords, a child of the same account, are the escape hatch if that ever grates.
- **No machine identity.** The host's own mail is aliases onto the human's mailbox; nothing else has an
  account. A site outage therefore costs *new logins*, never the queue (see below).

**The site grows three routes**, in the shell rather than the page pipeline, because each answers a
service rather than rendering a page:

| Route | Credential | Caller | Answers |
| --- | --- | --- | --- |
| `POST /internal/mail/auth` | **HTTP Basic, the credentials being checked** | Dovecot's Lua passdb | `200` match, `401` mismatch |
| `POST /internal/mail/lookup` | `Bearer` service token | the socketmap gateway (Decision 8) | one mailbox path or one alias target, or `404` |
| `POST /internal/mail/settings` | `Bearer` service token | Rspamd's `external_map` | the recipient's actions |

- **Basic on auth, because the caller is the user.** The route is presented with a username and a
  password and asked whether they are right — Dovecot's passdb in one sentence. The password stays in
  a header instead of a body, and the route needs no service token: the thing being verified is the
  thing presented. Dovecot's Lua has `dovecot.base64`.
- **One `Bearer` token for the rest**, compared in constant time, configured on the site and shipped
  root-only to the host; absent or blank stops the site at startup like any other policy value. It is
  the same shape as the view token (`ViewToken.HeaderScheme`) and the operator credentials a page can
  name, with a configured secret instead of a minted one. A caller without it gets `401`, counted by
  the existing throttling.
- **One credential per request, never two**, so there is no ambiguity about which layer said no: on
  auth, `401` is a wrong password and anything not `200` is a fault that tempfails; on the service
  routes, `404` is "no such address" and everything else defers. Neither logs its credential.
- **The auth route is a password oracle, and it is `/login`'s oracle.** Same account, same hash, same
  throttle, same rules; it answers `401` identically for an unknown address and a wrong password.
- **The userdb is local, so delivery never waits for the site.** Home, uid, gid and the mail location
  are `static` in Dovecot — mail-side constants — and the quota is uniform at 5 GB in config. If the
  userdb were a route, LMTP — which has no passdb — would put the website in the *delivery* path, and
  an outage would stall already-accepted mail. It must not: a finished `RCPT TO` is a promise.
- **A passdb cannot fill the userdb instead.** `auth_password_verify` may return a fields table, but
  with a userdb configured Dovecot still looks the user up; the round trip is saved by the userdb being
  local, not by the passdb being chatty.
- **None of this traffic goes through Caddy.** The mail containers call the app at a **mapped literal,
  `http://169.254.42.42:8080`**, which pasta translates to the host's `127.0.0.1:8080` — the address
  Caddy proxies to. Nothing off the host, no name, no TLS; plain HTTP is the same clear hop Caddy
  already makes. `127.0.0.1` is *not* the answer: inside the pod it is the pod's own loopback. The
  mapping is one option on the pod (`pasta:--map-host-loopback=169.254.42.42`), proven in
  [the spike](../spikes/mail-stack-preflight.md), and the host sees the request as coming from
  `127.0.0.1`.
- **`/internal/*` is refused at the edge.** Caddy answers `404` for the whole prefix in the website's
  site block. Defence in depth: the routes live on the app's loopback listener, and the edge rule makes
  a leaked token useless from the internet.

**What breaks when the site is down.** New IMAP and SMTP logins fail, and Postfix defers recipients at
`RCPT TO`, so senders hold the mail. Everything else continues: accepted mail delivers (the userdb is
local), Rspamd scores with the domain default, the quota applies. Nothing is lost, new mail is late,
and the queue is where an operator sees it. No break-glass credential exists by design — the first open
question asks whether that should change.

**Storage and quota.** Maildir, not mdbox: self-describing, recoverable with `rsync` and `grep`, which
matters more here than density. `mail_driver = maildir`, `mail_home = /var/mail/%{user}`,
`mail_path = ~/Maildir`. 5 GB per mailbox with a 90% warning; over quota LMTP tempfails and Postfix
queues. Uniform because delivery must not do I/O; when per-account quotas are wanted the value has to
come from something that survives an outage — a cached copy, not a live call.

**Aliases are rows**, served through `virtual`, and every one points at the same mailbox:

| Address | To | Why |
| --- | --- | --- |
| `postmaster@`, `abuse@`, `hostmaster@`, `webmaster@` | `osric@moosemorals.com` | RFC 2142 says these exist |
| `dmarc@`, `tlsrpt@` | `osric@moosemorals.com` | the reports Decision 12 asks for |
| `alerts@moosemorals.com` | `osric@moosemorals.com` | machine mail, filterable by address in the same inbox |
| `root@neith.moosemorals.com`, `root@moosemorals.com` | `osric@moosemorals.com` | every daemon that mails `root` |

No catch-all: an address that does not exist gets 550, which keeps backscatter off the domain.

### 8. Postfix asks a gateway we write

Postfix consults its maps synchronously, once per recipient, inside `RCPT TO`, and has no HTTP map
type. Teaching the website to speak [socketmap](https://www.postfix.org/socketmap_table.5.html) is the
one thing this plan will not do: a second protocol, with no framework support and no existing tests,
inside the process that serves pages.

So a fourth container, `mail-maps`, is a translator: socketmap in, HTTP out, no state.

- **One UNIX socket** in a pod volume (`socketmap:unix:/run/maps/socketmap:vmailbox`), because Postfix
  is explicit that socketmap is unauthenticated. It serves two names, `vmailbox` and `virtual`;
  `virtual_alias_domains` stays a static `neith.moosemorals.com`, since one stable name does not
  deserve a round trip.
- **Every query is one `POST /internal/mail/lookup`**, answered `200 {value}` or `404`. No snapshot, no
  cache, no volume: restarting it is invisible because there is nothing to lose.
- **Replies come straight from HTTP.** `200` → `OK`, `404` → `NOTFOUND`, and everything else — a
  timeout, a `5xx`, a refused connection, a mistyped token — → `TEMP`. No failure of ours may become a
  `5xx` at `RCPT`, which would bounce mail for a problem the sender did not cause.
- **`postmap -q` is the whole test story**:
  `postmap -q 'shopping@moosemorals.com' socketmap:unix:/run/maps/socketmap:virtual`.

The cost is that the site is in the *acceptance* path: two lookups per `RCPT TO`, each a keep-alive
round trip over loopback, and while the site is down or slow, mail is deferred rather than accepted.
At personal volume that is a few milliseconds; it is not free of availability, and a short-lived cache
in the gateway is the named escape hatch. Delivery is deliberately outside that path, which is why the
userdb is local.

### 9. Rspamd, advisory only

SPF and DMARC are DNS policies the receiver enforces; DKIM is the only one needing a signer. So one
milter verifies all three and scores:

| Concern | Traditional daemon | Here |
| --- | --- | --- |
| SPF | `pypolicyd-spf` | Rspamd `spf` |
| DKIM | OpenDKIM | Rspamd `dkim_signing` |
| DMARC | OpenDMARC | Rspamd `dmarc` |
| ARC | OpenARC | Rspamd `arc` |
| Spam | SpamAssassin + amavisd | Rspamd |

Rspamd rather than SpamAssassin because parsing and DNS are shared, per-recipient settings are
first-class, and the milter is a seam: `spamass-milter` slots in behind it and everything else stays.
The loss is `sa-update`; Rspamd's rules ship with the release, so the image is rebuilt monthly.

- **Inbound (25):** verify SPF, DKIM, DMARC, ARC; add `Authentication-Results`, `X-Spam-Status`,
  `X-Spam-Level`, `X-Spamd-Result`, and `X-Spam-Flag: YES` across the account's threshold.
- **Originating (587, 2525):** skip the spam checks, sign. Postfix passes
  `milter_macro_daemon_name=ORIGINATING`; 587 is matched by `authenticated = yes`, 2525 by its pinned
  address.
- **Never** reject, soft-reject, greylist, quarantine at SMTP or discard.
- **Fail-open:** `milter_default_action = accept`, so a dead Rspamd means untagged mail, not a queue.

Advisory is a property of the config:

1. `reject` and `greylist` are 1000 everywhere, including the domain default; `mail-check` fails a
   lower value, and a GTUBE message is *delivered*.
2. No greylisting, no connection-time DNSBL reject, no postscreen reject: global decisions about an IP.
   RBL results may still be score symbols.
3. Unknown recipients are still rejected and relay still denied — protocol correctness, not filtering.
4. Nothing is discarded: `Junk` is a folder, and there is no `Trash` action or Sieve `discard`.
5. No module may soft-reject, and with no Redis, ratelimit, greylist and replies cannot run.
6. The host's own mail is not scored at all, so nothing can file a nightly digest.

**Bayes runs on SQLite, and only ever adds score.** The classifier has four statfile backends — `mmap`,
`sqlite3`, `cdb` (read-only) and `redis` — and the shipped `backend = "redis"` is a default line, not a
requirement. `backend = "sqlite3"` with a `path` per statfile keeps the token counts in two files under
`/var/lib/rspamd`, learned and scored by the worker itself, with no daemon beside it:

- **Taught by folder moves, seeded once.** Moving a message into `Junk` learns spam, moving it back out
  learns ham, through Dovecot's `imapsieve` and a pipe to `rspamc`. The classifier says nothing until
  `min_learns` (200) is reached in both classes, which moves alone would take months to reach, so the
  first corpus comes from the mailbox itself: `make mail-learn SPAM=… HAM=…`.
- **Advisory like everything else.** The symbols score (`BAYES_SPAM` 4.0, `BAYES_HAM` -4.0) and can do
  no more: `reject` stays at 1000, so even a certain spam is a tagged message in `Junk`.
- **One corpus per account, keyed where the account is known.** `per_user = true`, with the string form
  of that setting: Rspamd compiles it as a Lua function, calls it with the task, and files the tokens
  under whatever it returns. Left alone, Rspamd's own key is
  `rspamd_task_get_principal_recipient` — the address as sent — so `shopping@` and `osric+shopping@`
  would each become a corpus of their own. The extractor drops any `+detail`, resolves what is left
  through the same `virtual` map the gateway serves, and returns the account's **Dovecot user name**
  (`osric`) — the thing that owns the mailbox and holds the password, not the address it arrived at.
- **Learning names the same key.** The pipe hands the sieve's `imap.user` to `rspamc -d`, which becomes
  `Deliver-To` and is what the extractor is handed first — so a learn and a delivery agree without a
  domain being written into a script anywhere, and a learn never depends on the message's `To:`.
- **Every account starts cold.** `min_learns` is 200 per class *per account*, so each human needs their
  own seed: `make mail-learn ACCOUNT=osric SPAM=… HAM=…`. Machine mail shares the human's account and
  trains with it.
- **No expiry, because the module is Redis-only.** `bayes_expiry` acts only on a Redis backend, so
  tokens are never pruned. At this volume a decade of moves is a few megabytes, and the reset is
  deleting both files and seeding again.
- **Not backed up.** The tokens are derived data, so the backup carries the mailbox and not the
  classifier (Decision 14).
- **The extractor declares its own map, because the named lookup does not work.** `get_maps()` returns
  an array rather than a name-keyed table, so `get_maps()['ALIASES']` is nil and the extractor quietly
  falls back to the recipient — the exact failure this design has to avoid.
  `rspamd_config:add_map{ type = 'map'; url = 'file://…' }` inside the `per_user` chunk is what works,
  and [the spike](../spikes/mail-stack-preflight.md) proves an alias, a plus address and a second
  account land in the corpora they should.
- **The state directory is the entrypoint's job.** The image does not create `/var/lib/rspamd/bayes`,
  and the classifier refuses to start without it.
- **Seeding reports its skips.** The shipped `learn_condition` declines a learn the classifier already
  agrees with, so `make mail-learn` prints how many of each class were taken.
- **`rspamadm configdump classifier` is the check** that the backend and both paths landed, in place of
  a config file to read.

### 10. Tuning per account, filing per address

Two questions, two units:

| | Unit | Because |
| --- | --- | --- |
| How much spam this reader tolerates | **the account** | a property of the human, not the hat |
| Which folder a message lands in | **the address** | the point of handing out an address |

**Tuning is per account, and the addresses behind it are computed.** Rspamd matches the recipient
against the settings, so the settings for a mailbox match its address, its aliases and its plus pattern
(`/^osric\+.*@moosemorals\.com$/`, since plus addresses are never aliases). The endpoint answers
directly — Phase 0 proved the selector names the SMTP recipient
([spike](../spikes/mail-stack-preflight.md)) — and sends `{"rcpt":["…"]}`, an array even for one
recipient. **The answer may never contain `actions`**: a settings response can lower `reject`, which is
the one invariant the plan treats as absolute, so the endpoint emits scores, symbols and thresholds
only, and `mail-check` asserts the shape of what it returns. A static domain default is the floor:

```hcl
"default" {
  rcpt = "/@moosemorals\\.com$/";   # backslashes doubled: UCL parses the string first
  priority = low;
  apply { actions { reject = 1000.0; greylist = 1000.0; "add header" = 4.5; } }
}
```

`"add header"` is the score at which an account sees `X-Spam-Flag: YES`; `1000.0` keeps everything in
the INBOX while adding the score headers. `want_spam = yes` is the full bypass for an account that must
never be filed, which also means no DKIM signing. One block per message, so only a message spanning two
accounts has a corner, decided by priority. Bayes follows the same unit — one corpus per account
(Decision 9) — because a corpus is evidence about one person's mail; the account's threshold is what
that evidence is worth.

**Filing is per address**, and which hook applies is what survives to delivery:

| Address handed out | Envelope at delivery | Match on |
| --- | --- | --- |
| `osric+shopping@` | `osric+shopping@` | `envelope :detail "to" "shopping"` |
| `shopping@` | `osric@` | `header :contains "X-Original-To" "shopping@"` |

A plus address needs no header, which is why Decision 5 keeps the detail. A free-standing alias is
rewritten by definition, so `X-Original-To` — added before any aliasing — is the only record of what
was sent, and the address-leak audit trail:

```sieve
require ["envelope", "fileinto", "mailbox"];

if envelope :detail "to" "shopping" { fileinto :create "Shopping"; stop; }
if header :contains "X-Original-To" "newsletters@" { fileinto :create "Newsletters"; stop; }
if header :is "X-Spam-Flag" "YES" { fileinto :create "Junk"; }
```

The folder list stays hand-written — a folder is a naming decision — and `mail-check` asserts no rule
names an address that is neither an alias nor the plus form of a mailbox.
`lmtp_save_to_detail_mailbox` would file by detail with no Sieve and is deliberately off: the folder
name would come from whatever the sender typed.

### 11. TLS from Caddy's store

The certificate makes 993 usable from a client; it is not what makes the local corridor work, which is
why Phase 2 can read over `ssh -L 993:127.0.0.1:993` before DNS exists. Once the name does, Caddy
obtains and renews it — the CAA record allows it — from a site block that exists only to say there is
no website here:

```caddyfile
# deploy/Caddyfile — the mail host has no website. It exists for MX, IMAP and its certificate.
neith.moosemorals.com {
	respond "there is no web content on the mail host" 410
	header -Server
	header -Via
}
```

410 rather than 404: there is no other path to try and this name is not a placeholder. The one thing
that must answer is ACME's HTTP-01 probe, which Caddy serves on port 80 ahead of site routes — the
matrix asks for a bogus challenge path to get Caddy's `404` rather than the `410`, the cheapest way to
prove it.

The daemons cannot read Caddy's store, so a root timer copies the leaf into `vmail`'s home (`0644`
certificate, `0640` key, `vmail:vmail`), compares hashes, and reloads Postfix and Dovecot through
`vmail`'s user manager when it changed. Containers mount that directory read-only. `mail-check` asserts
the copy exists, matches what Caddy serves, and is readable by the daemons.

The timer is contingent: Caddy is rootful only because it is the last system unit, and
[`caddy-is-still-rootful`](../backlog/caddy-is-still-rootful.md) is the item that frees it. This plan
works either way and depends on neither order.

ACME stays on HTTP-01 although the host holds a Mythic Beasts key: DNS-01 would need
`caddy-dns/mythicbeasts` compiled into Caddy, a custom build of the one image the site depends on, to
remove a one-line site block and a dependency on a port that is already open.

### 12. The records, and the host that publishes them

| Name | Type | Value | Needed for |
| --- | --- | --- | --- |
| `neith` | `A` / `AAAA` | the VM's own addresses | everything public |
| both addresses | `PTR` | `neith.moosemorals.com` | outbound reputation |
| `@` | `MX` | `10 neith.moosemorals.com.` | inbound mail |
| `@` | `TXT` | `v=spf1 ip4:<vm-ipv4> ip6:<vm-ipv6> -all` | outbound, from a human address |
| `neith` | `TXT` | `v=spf1 ip4:<vm-ipv4> ip6:<vm-ipv6> -all` | outbound, from the machine |
| `mail._domainkey` | `TXT` | `v=DKIM1; k=rsa; p=…` | signing as `moosemorals.com` |
| `mail._domainkey.neith` | `TXT` | `v=DKIM1; k=rsa; p=…` | signing as `neith.moosemorals.com` |
| `_dmarc` | `TXT` | `v=DMARC1; p=none; rua=mailto:dmarc@moosemorals.com` | oversight; `ruf` waits for outbound |
| `_smtp._tls` | `TXT` | `v=TLSRPTv1; rua=mailto:tlsrpt@moosemorals.com` | optional, useful |
| `_mta-sts` | `TXT` | `v=STSv1; id=…` | later, with the HTTPS policy file |

None of it is needed for the local corridor.

**Two SPF records, one per sending name, with literal addresses.** SPF is evaluated against the
envelope sender's domain, so the subdomain needs its own and the apex one is *edited* — two SPF records
at one name are a permanent error. Both name the addresses with `ip4:`/`ip6:` rather than `mx:`/`a:`,
because a literal costs the receiver no lookup, where `mx` costs one against a limit of ten and makes
every receiver resolve a name that only has those two answers, differently on v4 and v6. The objection
— a literal goes stale when the VM moves — is answered by `dns-sync`: it fills both values from the
Hetzner API, the same answer it uses for `A`/`AAAA` and the PTR, so moving the server updates the
address and the authorisation in one run. `dig` checks the record still names the live addresses.

**No CNAME at the mail host:** `neith` must be an address record or MX resolution and PTR alignment
both break.

**DMARC is inherited and that is enough.** `_dmarc.moosemorals.com` covers the subdomain, and a
signature made as `neith.moosemorals.com` aligns with the organisational domain under the default
relaxed alignment. `rua` only, because `ruf` is a message we would have to send. The ramp is `none` for
two weeks, then `quarantine`, then `reject`; SPF has no ramp, because it names the only two addresses
that will ever send, so `-all` is exact on day one. Two things can make it wrong, both visible: the VM
moving, which `dns-sync` follows, and a smarthost, whose addresses join the record before the first
relayed message.

**DKIM: 2048-bit RSA, one key per sending name.** [RFC 8301](https://www.rfc-editor.org/rfc/rfc8301)
makes 1024 the floor and 2048 the recommendation; nothing beyond 2048 fits a 256-octet TXT record
reliably. Both private keys stay `0640` under `vmail`, never in git — `dns-sync` reads the public half
beside each.

**Rotation is a year, and immediately on suspicion.** No standard sets an interval;
[RFC 6376](https://www.rfc-editor.org/rfc/rfc6376) leaves key choice to the operator and asks only that
the old public key stay published for a validation interval, because a verifier may defer the check —
DKIM's only revocation is the record disappearing. The three-, six- and twelve-month figures are vendor
practice; a month is not anyone's recommendation. Rotation cannot rescue a compromised host: the box
holds the key *and* the credential that publishes its own record, so revoking a leak is out-of-band —
revoke the API key, delete the selector, mint a new one, read the reports.

**The host publishes all of it** through [the Mythic Beasts DNS API
v2](https://www.mythic-beasts.com/support/api/dnsv2): HTTP with the API key, records addressed as
`/zones/{zone}/records/{host}/{type}`, `PUT` replacing an RRset and `POST` adding, and `verify=true`
making the API compare its authoritative answers with what it holds. Keys are restrictable to zones,
records and types, and that is what makes the rest safe: **this key writes exactly the mail records** —
the apex `MX` and `TXT`, `neith`'s `A`, `AAAA` and `TXT`, and the `_domainkey`, `_dmarc` and
`_smtp._tls` records — so the worst a compromised mail host can do to DNS is lie about itself. The
apex `A`/`AAAA`, the `CAA` record and the site's names stay the owner's; widening is
[`dns-manages-only-mail-records`](../backlog/dns-manages-only-mail-records.md), on the backlog.

`deploy/dns/records` is the declaration; `tools/dns-sync` is the reconcile — a small .NET tool with
NUnit tests and `Core.Text.JsonSerialization`, run from a small image on a host timer, because the host
holds the credentials and nobody should have to be involved. It reads only the pairs it declares, and:

- **adds and updates, never deletes.** `PUT` replaces a whole RRset, so it reads first and keeps records
  it did not declare — an apex TXT can be someone's verification token. A dropped declaration removes
  nothing until `--prune`, which prints what will go.
- **sets the PTR in the same run**, through the Hetzner token the deploy already uses, so the forward
  name, the reverse name and the SPF that names the address agree by construction, and the run fails if
  they do not.
- **verifies before reporting success**, polling `verify=true`. `make mail-dns` shows the plan;
  `APPLY=1` writes it.

The credential is `deploy/mythic-beasts.token`, matched by the `*.token` ignore rule, installed `0600
root:root`, never in the image.

### 13. Deliverability, and what port 25 gates

All of this concerns the second corridor.

- **PTR before MX.** A receiver that cannot reverse-resolve `neith` treats the first message with
  suspicion whatever SPF and DKIM say.
- **Outbound 25 gates only sending to other people**, and waits for Phase 6. Test from the host then; if
  it is blocked, ask Hetzner or relay through a smarthost on 587.
- **Inbound 25 is separate**, and must be open before an `MX` exists: `ufw` and Hetzner's firewall if
  one is enabled, tested from outside.
- **IPv4 first.** Publish AAAA but set `smtp_address_preference = ipv4`; a new IPv6 reputation is
  harder to read. Revisit when the reports are clean.
- **Warm-up.** Personal mail first, then Gmail, Outlook and `mail-tester.com`, reading
  `Authentication-Results` and the blocklists before anyone is told the address exists.
- **No bulk mail.** A newsletter from this host is a reputation problem, not a feature.

### 14. Backups

The mail volume and the DKIM key. **Not** the accounts, aliases or settings — those are in
`storage.db`, which the site already backs up, and a second copy would be the drift Decision 7
removes. The Postfix queue is not worth backing up; a restore that loses it is a retry. The Bayes statfiles are
derived data too — a restore reseeds them from the mailbox it just restored (Decision 9).

`doveadm backup` for an online copy, or stop the pod and archive; off-host and encrypted, because a copy
on the same disk is not a backup. Daily incremental, weekly full, thirty days. The restore test
restores the volume *and* the site's database into scratch and reads a message over IMAP: a Maildir
without `storage.db` is a mailbox nothing can log in to.

### 15. Monitoring, logs and health

- **The local corridor is the first monitor**: `unattended-upgrades` mails `alerts@`, and one
  `OnFailure=` unit mails it when the app or Caddy fails — [`ops-no-outage-notification`](../backlog/ops-no-outage-notification.md)
  answered for everything except the case where mail itself broke.
- **It cannot alert about itself.** Mail about a broken host travels through that host, so a dead pod or
  a full disk is silent. Out-of-band alerting is its own problem.
- Containers log to `vmail`'s user journal; `maillog` goes to stdout and nothing writes log files
  inside a container. Healthchecks: `postfix status` or a TCP probe, a probe on 993 plus
  `doveadm service status`, `rspamadm control ping`.
- **A queue alert matters more than a CPU alert**: `postqueue -p` non-empty for an hour, or more than 50
  messages, means delivery is failing.
- **The settings answer is checked, not trusted.** `mail-check` asserts it carries no `actions` block,
  because a response that did could make Rspamd reject a message the plan says can never be rejected.
- **The auth route's p99 is a mail number**, because every login waits on it. Watch it; cache in the Lua
  script rather than adding a credential store.
- `auth_failure_delay = 2 secs` slows credential stuffing; if the journal shows a sustained attack, add
  fail2ban rather than changing the ports.

### 16. Deployment shape

```
deploy/mail/
  Containerfile              # four targets: postfix, dovecot, rspamd, mail-maps
  mail.pod                   # 25/587/993 published through pasta, 2525 on loopback
  postfix.container
  dovecot.container
  rspamd.container
  maps.container             # the socketmap→HTTP gateway: no state, no volume
  maps.conf                  # socket path, lookup URL, token path, timeout
  cert-sync.sh               # root: copies Caddy's leaf into vmail's home, then reloads
  mail-cert-sync.timer       # system timer, root
  postfix/
    main.cf
    master.cf
  dovecot/
    dovecot.conf
    auth-http.lua            # passdb only: presents the credentials as Basic
    sieve/before.sieve
    sieve/learn-spam.sieve     # imapsieve: into Junk pipes to `rspamc learn_spam`
    sieve/learn-ham.sieve      # ... and back out of it pipes to `rspamc learn_ham`
    sieve/learn.sh             # the pipe target both call
  rspamd/
    statistic.conf             # the classifier: sqlite3 backend, two statfiles under /var/lib/rspamd
    local.d/*.conf
    settings.d/00-default.conf # the floor; a rendered 90-accounts.conf only if the spike needs one
  dkim/                      # not in git; <domain>.mail.key, vmail, 0640
  service.token              # not in git (`*.token`); what the service routes check
deploy/dns/
  records
  Containerfile              # tools/dns-sync
  dns-sync.container         # `podman run --rm`, from the timer
  mail-dns.timer
deploy/host/
  msmtprc                    # 0600 root:root
  mailname                   # neith.moosemorals.com
  vmail-user.sh              # useradd, subuid range, enable-linger — idempotent, installed
  25-unprivileged-ports.conf # the floor of 25 — installed
  25-unprivileged-ports.conf # net.ipv4.ip_unprivileged_port_start=25
```

`vmail` is created by that script: home, `/usr/sbin/nologin`, `/etc/subuid` and `/etc/subgid` ranges
(rootless podman needs one even when nobody logs in), `loginctl enable-linger vmail`. `make mail-deploy`
drives the units through `vmail`'s own user manager, because the account has no shell to log in with —
`sudo -u vmail -- env HOME=/home/vmail XDG_RUNTIME_DIR=/run/user/<uid> systemctl --user …`, from a
directory `vmail` can read — and `mail-check` reads `vmail`'s journal; the cost of two users is that
every mail command names one, and the cost of no shell is that every command spells out the runtime
directory.

Images come from `debian:13-slim` pinned by digest, exact package versions, `--timestamp` from the
commit. Debian's `postfix` needs a debconf preseed so the build does not prompt; the runtime config
replaces `/etc/postfix` wholesale. Dovecot installs `dovecot-core dovecot-imapd dovecot-lmtpd
dovecot-sieve dovecot-lua` — not `dovecot-pop3d` — and Rspamd installs `rspamd` only, with no Redis
anywhere. Host packages come from Debian like `podman` and `ufw`: `msmtp` and `msmtp-mta`, with
`mail-deploy` failing if `/usr/sbin/sendmail` is not theirs.

| Target | Effect |
| --- | --- |
| `make mail-image` | build the four images and `dns-sync`, tagged `GIT_SHA` |
| `make mail-deploy HOST=…` | create or update `vmail`, install packages and the sysctl, push pod and configs, install `msmtprc`/`mailname`/`service.token`, reload, restart, wait for health |
| `make mail-check HOST=…` | the three config checks, thresholds, the socketmap answering, a plus address resolving, certificate and key permissions |
| `make mail-box-add ADDRESS=…` | give an existing account its mailbox address; creating people stays `make user-add` |
| `make mail-alias-add ADDRESS=… TO=…` | point another address at a mailbox; one row |
| `make mail-maps KEY=… MAP=…` | `postmap -q` against the gateway: "does Postfix know this address?" |
| `make mail-dkim DOMAIN=… SELECTOR=…` | generate a key and its public half; never overwrite; then `mail-dns` publishes it |
| `make mail-dns [APPLY=1]` | diff the declared records and the PTR, print, write with `APPLY=1`, verify |
| `make mail-probe TO=…` | submit through the loopback listener and report where it landed |
| `make mail-learn ACCOUNT=… SPAM=… HAM=…` | teach one account's Bayes corpus from two directories of messages, through `rspamc` |
| `make mail-logs` / `mail-status` | user-journal tail / pod and container health |
| `make mail-backup` | online `doveadm backup` for the off-host copy |

`mail-deploy` is a gate: validate every config before restarting, check that the forward name, the PTR
and `myhostname` agree, and report success only when every container is healthy and `mail-probe` has
delivered. It must not touch the website's units — and it does not: they are another user's.

Excerpts are in [the sketches](email-configuration.md).

## Phases

Dependencies, not components: the site answers before the mail host asks it anything, the corridor
inside the host comes next, and the public half follows. Every phase is useful when it lands.

Each phase carries a level from [the backlog's difficulty scale](../backlog/README.md#difficulty-is-a-level-not-a-duration)
— a rank of the work, never a promise about the calendar. Phase 8 has none because its items are
work for other plans; each names its own level where it is filed.

### Phase 0 — pre-flight (`caffeinated-squirrel`)

- Read the VM's addresses from the Hetzner console; record the current SPF and the absent MX.
- Confirm the quota arithmetic: 5 GB per mailbox against 80 GB shared with everything else.

**Measured on the dev box, and done** — [the spike](../spikes/mail-stack-preflight.md) has the commands
and the numbers:

- Pasta reaches the site's loopback at a mapped literal, not `127.0.0.1`, and the pod carries the
  option; the host sees `127.0.0.1` as the client.
- A published port is forwarded by pasta, which refuses 25 until the sysctl is lowered and preserves the
  source address, so the host's own address is what `mynetworks` names.
- Per-account Bayes keys an alias, a plus address and a whole account correctly, through
  `rspamd_config:add_map` inside the `per_user` chunk.
- Dovecot 2.4 wants `userdb_fields` for the static userdb's values, `%{user | lower}` for the username
  format, and a Lua passdb can name the account; `+detail` is split by LMTP and never by auth.

**Measured on the host, and left in place:**

- `vmail` exists — uid 988, no shell, `/home/vmail` `0700`, subuid range `296608`, `Linger=yes` — and
  `deploy/host/vmail-user.sh` installs it idempotently.
- `net.ipv4.ip_unprivileged_port_start` is 25 from `deploy/host/25-unprivileged-ports.conf`, and a pod
  published 25 as `vmail` from a container that was then removed.
- A remote client's address survives the publication (`81.141.12.20` v4, `2a00:23c7:…` v6), and a v4
  `PublishPort` serves IPv6 to any listener that accepts it — Postfix does.
- Caddy's leaf copies into `vmail`'s home and is readable by the daemons there; it is **EC P-256**, and
  currently SANs `moosemorals.com` alone.
- A quadlet in `vmail`'s config is generated and active with no session, so linger carries it.
- **`sudo -iu vmail` does not work** — `nologin` refuses `-i` — so every mail command names
  `XDG_RUNTIME_DIR=/run/user/988` and a directory instead.
- Rspamd's settings selector is `id('rcpt');rcpts('smtp'):addr`, and its answer may never carry
  `actions`.

**Still open in this phase:**

- **The Mythic Beasts token does not exist yet.** It is created in the control panel; until then the
  key's scope is untested and Phase 3 waits.
- **A reboot**, to see a `vmail` quadlet come up without a session — the last piece of the linger claim.
- The certificate the root timer copies, which is Phase 2's script rather than a spike.

### Phase 1 — the website answers (`determined-mountain-goat`)

- Mail records in the storage model: a mailbox address on the account, aliases pointing at it,
  per-account settings.
- `POST /internal/mail/auth` on the loopback listener, Basic with the submitted credentials: `200` for
  a match, `401` for a mismatch, `5xx` for a fault. `POST /internal/mail/lookup` and
  `POST /internal/mail/settings` behind the `Bearer` token.
- `https://moosemorals.com/internal/*` answers `404` at the edge.
- Tests where the site already tests: wrong password, revoked password, unknown address, missing or
  wrong token, and the response shapes the callers rely on.
- [shell](../shell.md) and [architecture](../architecture.md) gain the section.

### Phase 2 — the local corridor (`persistent-salmon`)

- Build the images; all three config checks pass.
- The loopback listener accepts from `msmtp`; LMTP delivers into Maildir; the Lua passdb authenticates
  against the site; `postmap -q` against the gateway answers for a mailbox and an alias.
- `sendmail root` arrives, and `osric+probe@` with the detail intact.
- Wire `unattended-upgrades` and one `OnFailure=` unit, and leave them wired.
- Read the mailbox over `ssh -L 993:127.0.0.1:993`. Nothing published, nothing in DNS, no Rspamd.
- No POP3: `ss -ltn` and `doveconf -n protocols`.

### Phase 3 — a name, a certificate and IMAP (`caffeinated-squirrel`)

- `make mail-dns` publishes `neith` and the PTR; the Caddy block goes in; 993 is published and the
  mailbox is read from a client with a valid certificate.
- `dns-sync` earns its keep or does not: if diff-and-verify is more trouble than `dig` and a control
  panel, say so before the mail records depend on it.

### Phase 4 — inbound mail from other people (`eager-beaver`)

- Open 25 and 587 in `ufw` and cloud-init; add `smtp` and `submission`; add the `MX` last, after the
  host answers on 25 from outside.
- SPF at both names; DMARC `p=none` with `rua`.
- `swaks` from elsewhere: delivered; unknown recipient 550; submission without SASL refused.
- A new alias on the site is accepted at `RCPT TO` on the next message; stopping the site defers rather
  than answering 550.

### Phase 5 — Rspamd and advisory filtering (`caffeinated-squirrel`)

- Milter on 25, 587 and 2525; `Authentication-Results`; DKIM signing for submission.
- GTUBE delivered, scored, tagged, never rejected. Stop Rspamd: mail still arrives, untagged.
- Tuning: an account on the default, one with settings, one at `1000`; a new alias of a tuned account
  gets that account's settings with no host edit.
- Stop the site: new mail defers, queued mail still delivers, scoring falls to the default.
- Bayes: `make mail-learn ACCOUNT=…` seeds that account, `X-Spamd-Result` names `BAYES_SPAM` on a
  learned message, and a message moved into `Junk` teaches it from then on.
- Bayes is per account: what is learned in one account's `Junk` moves another account's score not at
  all, and every alias and plus form of one account shares its corpus.
- `rspamadm configdump classifier` says `sqlite3` and both paths; deleting the statfiles leaves mail
  arriving and scoring, just without a Bayesian verdict.
- The host's own mail is `ORIGINATING`, unscored and signed.

### Phase 6 — outbound to other hosts (`eager-beaver`, and only when the owner asks)

- Test outbound 25; ask Hetzner or set `relayhost` to a smarthost, whose addresses join both SPF
  records first — DKIM keeps DMARC aligned either way.
- `swaks` to Gmail and Outlook; the signature verifies at `check-auth@verifier.port25.com`.
- Then the ramp: `p=none` → `quarantine` → `reject`, `ruf` added once reports can be sent.
- `mail-tester.com` and the blocklists before the address is told to anyone.

### Phase 7 — hardening and operations (`eager-beaver`)

- Backups, the restore test, queue monitoring.
- `auth_failure_delay`; review the journal for brute force; decide on fail2ban now that auth is one HTTP
  call from the inside of the host.
- Watch the auth route's p99.

### Phase 8 — follow-ups, each its own plan and its own level

- **Admin pages for mail**: accounts, aliases and settings, editing the rows Phase 1 created.
- **Webmail as a page capability**: an IMAP capability in the page contract. Two host users mean two
  podman networks, so the first question is the path the app takes to IMAP.
  [`whim-mcp-server-tools`](../backlog/WHIMS.md) already has the mailbox as its example.
- **DNS for the whole zone**: [`dns-manages-only-mail-records`](../backlog/dns-manages-only-mail-records.md).
- Bayes tuning: autolearn thresholds, token expiry (a Redis-only module today), and a per-account
  reset that drops one account's tokens without touching anyone else's.
- Allow/deny lists per address, as rows the tuning reads.
- MTA-STS and DANE if the zone is signed.

## Test matrix

| Test | Expected |
| --- | --- |
| `printf 'Subject: probe\n\nhi' \| sendmail root` | accepted on 2525, in the owner's INBOX, `From: alerts@moosemorals.com` |
| The same with the pod stopped | `sendmail` exits non-zero; nothing is queued |
| A message to `root@neith.moosemorals.com` via 25 | aliased to the owner, not rejected as unknown |
| A message to `osric+probe@moosemorals.com` | in `osric@`'s mailbox with no alias line; detail `probe`; `X-Original-To` says `osric+probe@` |
| `doveadm user osric+probe@moosemorals.com` | the same user as `osric@moosemorals.com` |
| A message to `osric+shopping@` | filed into `Shopping` by `envelope :detail`, scored by the account's settings |
| `postmap -q '…:virtual'` for an alias | the owner's mailbox |
| `postmap -q '…:vmailbox'` for a mailbox | its path |
| A brand-new alias added on the site | accepted on the next message; judged by its account; no host edit |
| A brand-new account with no settings rows | the domain default applies |
| `mail-maps` stopped, then a message to a known address | deferred, not bounced; delivered when it returns |
| The site stopped, then a message to a known address | `4xx` at `RCPT`; the sender retries |
| The site stopped, with mail already queued | delivered — the userdb is local, so the queue drains |
| The gateway's token is wrong | `4xx` at `RCPT` and a log line, never a `5xx` |
| `curl -i https://moosemorals.com/internal/mail/lookup`, with or without a token | `404` from Caddy |
| `curl -i http://127.0.0.1:8080/internal/mail/lookup`, no token | `401`, from the host |
| `POST /internal/mail/auth`, right password | `200`, and Dovecot logs in |
| The same with a revoked password, or none | `401`, identical for an unknown address and a wrong password |
| `POST /internal/mail/auth` while the database is down | `5xx`, so Dovecot tempfails rather than saying no |
| `ss -ltnp` | 25/587/993 held by `pasta`; 2525 and 11334 on `127.0.0.1` |
| `sudo -u vmail -- env HOME=/home/vmail XDG_RUNTIME_DIR=/run/user/988 systemctl --user status mail-pod` | running as `vmail`; no rootful mail unit; nothing owned by the website's user |
| `swaks --server neith.moosemorals.com --port 25` | 250, in the Maildir |
| Same, unknown recipient | 550 at RCPT, nothing queued |
| `swaks --port 587` with SASL + STARTTLS | accepted; DKIM signature present |
| `swaks --port 587` without SASL | 5xx, no relay |
| `openssl s_client -connect neith.moosemorals.com:993` | its certificate, IMAP banner |
| `curl -i https://neith.moosemorals.com/` | `410 Gone`, no `Server` header |
| `curl -i http://neith…/.well-known/acme-challenge/bogus` | Caddy's `404`, not the catch-all's `410` |
| `doveconf -n protocols` | imap + lmtp only |
| GTUBE to a `junk` account | delivered, `X-Spam-Flag: YES`, in `Junk` |
| GTUBE to an `inbox` account | delivered to INBOX, score headers, no flag |
| `podman stop mail-rspamd`, then send | delivered, untagged, not deferred |
| `rspamc < message.eml` | `add header` or `no action`, never `reject` |
| `rspamadm configdump classifier` | `sqlite3`, two statfiles under `/var/lib/rspamd` |
| A message moved into `Junk`, then the same message again on 25 | `BAYES_SPAM` in `X-Spamd-Result` |
| The same message delivered to a second alias of that account | the same `BAYES_SPAM`: the corpus is the account's, not the address's |
| A message learned in one account's `Junk`, then delivered to another account | no `BAYES_SPAM`: the corpora are separate |
| `rspamc stat` | one user row per account that has learned, with its own learns count |
| Both statfiles deleted, then mail | arrives and scores; no `BAYES_*` symbol; nothing fails |
| DKIM at `check-auth@verifier.port25.com` | `dkim=pass`, `spf=pass`, `dmarc=pass` |
| Rotate to a new selector | new mail verifies under it, old mail while both exist, the old record gone after `--prune` |
| `dig TXT moosemorals.com` | exactly one SPF, the live `ip4:`/`ip6:`, other apex TXT untouched |
| `dig TXT neith.moosemorals.com` | the same two addresses, `-all`, no `mx` or `a` |
| `dig -x` both addresses | `neith.moosemorals.com` |
| `make mail-dns` with nothing changed | no writes; empty diff |
| `make mail-dns` after editing a value | prints both RRsets; writes with `APPLY=1`; `verify=true` passes |
| The API key asked to write the apex `A` | `403`; unchanged |
| Restore a mailbox plus `storage.db` into scratch | IMAP lists it; counts match |

## Risks

- **One password is the whole identity.** Mail and the editor share an account and a password: a
  password phished from a mail client is a way into the site, a change must be repeated on every device
  that reads mail, and deleting the account stops its mail at once. The owner chose this; app passwords
  on the same account are the escape hatch, not a second account set.
- **The website is in the login and acceptance path.** Every login and every `RCPT TO` is a POST, so a
  site outage stops new sessions and defers new recipients, and the site's p99 is a mail number.
  Delivery is deliberately outside that path, so nothing is lost — but mail-acceptance uptime is the
  site's uptime. A cache in the gateway is the named escape hatch; the plan starts with none.
- **One more process between Postfix and the answer.** A wedged `mail-maps` defers mail exactly as an
  unreachable site does. `Restart=always` and a healthcheck bound it; the UNIX socket is why nothing
  outside the pod can ask it anything.
- **The private route depends on pasta.** Reaching the app's loopback from `vmail`'s pod is a passt
  capability, not a guarantee; Phase 0 proves it, and the fallback is the public name behind a
  loopback-only edge rule.
- **Outbound is the deferred risk.** A new address, an unset PTR or a blocked 25 fails mail to other
  hosts however perfect the stack is. Bounded to Phase 6.
- **The local corridor cannot alert about itself**, and `msmtp` does not queue, so a notification during
  a pod outage is gone.
- **The port sysctl is host-wide**; one human and one service account bound it, a second tenant ends it.
- **The port publisher is a layer that can break.** 25/587/993 are bound by pasta, not the container;
  it preserves the client's address, which Phase 0 measured, and `ss -ltnp` plus an outside `swaks`
  are the checks that it is still doing so.
- **The pinned Rspamd decides the settings door.** Merging a config rule with a per-message HTTP header
  is 4.0.1+ behaviour; before that the first source to answer won.
- **The per-account key is derived, not given.** Rspamd hands the extractor the address as sent, and the
  account behind an alias is a lookup; a stale or broken map falls back to the recipient, which splits
  the corpus by address without failing anything visible. The matrix tests an alias and a second
  account for exactly that reason, and the fallback if the extractor cannot read a map is the open
  question below.
- **Bayes on SQLite is the non-default path.** The classifier is two files that every worker opens, so
  contention is a lock rather than a protocol; `database is locked` in the log is the signal that this
  volume has outgrown it, and going back to Redis is one line and one container.
- **Dovecot 2.4 config drift.** Most tutorials are 2.3; use `doveconf -n`.
- **Certificate sync** couples Caddy's layout to `vmail`'s home; `mail-check` asserts the copy.
- **Single host, no HA.** Restore from backup is the answer; senders retry for days.
- **Provisioning needs the admin pages** until Phase 8: today a row and a Make target.
- **Rspamd's rules age with the image**; a monthly rebuild is part of operations.
- **Three credentials on the host** — the Mythic Beasts key (narrow), the Hetzner token (project-wide,
  so sharper) and the service token (guards every mail route) — plus the users' own passwords, which
  are now the site's too.

## Deliberately not doing

- **POP3.** No package, no listener, no option.
- **Redis**, and the modules that only run on it.
- **Greylisting, DNSBL rejects, postscreen rejects, SMTP-time quarantine.** Global and connection-level.
- **SpamAssassin**, the fallback behind the milter seam rather than a second scorer.
- **A single do-everything container**, the documented fallback.
- **A second set of people.** No separate mail accounts, password table or lockout.
- **A socketmap endpoint inside the website.** The gateway exists so the site stays HTTP-only.
- **Webmail, CalDAV/CardDAV, mailing lists, catch-all.** Different products; webmail is a later page
  capability.
- **DNS-01 for the certificate.** It needs a custom Caddy build; HTTP-01 works.
- **Deleting DNS records automatically.** Pruning is an explicit, printed run.
- **`lmtp_save_to_detail_mailbox`.** It would let a sender name your folders.
- **DANE and MTA-STS in the MVP**, a later Caddy file and a signed zone respectively.

## Validation

Done means these are true on the deployed host. The first group is the revision's reason for existing.

1. `sendmail root` arrives, is read over IMAP, and reads `From: alerts@moosemorals.com`;
   `unattended-upgrades` and one `OnFailure=` unit have delivered a real message.
2. `make mail-check HOST=…` passes: the three config checks, thresholds, the socketmap answering, a
   plus address resolving, certificate and key permissions.
3. `make mail-dns` twice is a no-op; `APPLY=1` then `verify=true` passes; the key cannot write the apex
   `A`.
4. From elsewhere: `swaks` on 25 delivers; `openssl s_client` logs in on 993 with a valid certificate;
   `https://neith.moosemorals.com/` answers `410`; `https://moosemorals.com/internal/*` answers `404`.
5. Unknown recipient 550; SASL-less submission 5xx; submission with SASL delivers and its DKIM verifies.
6. GTUBE is delivered to INBOX or `Junk` per the account, with the score headers.
7. `X-Spamd-Result` names `BAYES_SPAM` on a learned message; `rspamadm configdump classifier` shows
   `sqlite3` and two paths; deleting the statfiles leaves the filter working.
8. With `mail-rspamd` stopped a message still arrives, and the journal records the milter failure.
9. `dig MX`, both SPF records naming the live addresses, both `_domainkey`, `_dmarc` and `dig -x` all
   return the intended values; the PTR matches `myhostname`; `mail-tester.com` reports all three pass.
10. A mailbox plus `storage.db` restore into scratch and open over IMAP, counts matching.
11. `sudo -u vmail -- env HOME=/home/vmail XDG_RUNTIME_DIR=/run/user/988 systemctl --user status mail-pod`
    is healthy; `ufw` allows 25, 587, 993 and
    nothing else new; 2525 listens only on `127.0.0.1`.
12. Nothing listens on 110, 143, 465 or 995.
13. The website still deploys and answers `/health`; its units, network and volumes are untouched, and
    the only new surface is the three internal routes, which the edge refuses.

## Answers and open questions

The owner answered the questions this plan opened with on 2026-11-23. They are decisions above; listed
here so the record shows they were asked:

1. **Rootless, if we can** — Decision 2, as `vmail`.
2. **Several mailboxes from the start**, though it is one person — and one account is one mailbox is
   one human, with several aliases.
3. **Notification mail shares the account**, with an `alerts@` alias.
4. **Maildir**, not mdbox.
5. **The host manages its own DNS**, starting with only the mail records; the rest of the zone is on
   the backlog.
6. **Everything in the database from day one**, with the site growing the three routes; the pages that
   edit those rows are their own plan.
7. **5 GB per mailbox**, one person, 80 GB disk.
8. **Webmail will exist**, as an IMAP capability in a page — its own plan.
9. **Outbound waits**: Hetzner in a couple of months; nothing before Phase 6 depends on it.

Still open:

- **Break-glass.** No local credential by design, so a site outage locks the owner out of new logins.
  Live with it, or keep a local copy of *the same* account's password — a passwd-file entry or a
  Dovecot auth cache — ahead of the Lua passdb? A second *account* is not on the table; this is only
  where a copy of one password may live.
- **Which way Rspamd's settings go** — endpoint, or an export it renders from — is Phase 0's spike.
- **The app's path to IMAP** for webmail: a listener both users can reach, or the public name with a
  service credential.
- **Retention** once 5 GB is not just a start.
- **What per-account Bayes falls back to** if the `per_user` extractor cannot read a cached map at
  scoring time: one Rspamd per human, which is honest and heavier, or a corpus keyed on the address as
  sent, which is not per account at all.
