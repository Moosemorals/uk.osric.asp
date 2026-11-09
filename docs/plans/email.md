<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Email for moosemorals.com

Status: **proposed, not implemented**. Written 2026-11-06. This is a design and an
implementation plan, not a record of work done. It assumes the host described in
[deploy/README.md](../../deploy/README.md) and does not change the website.

The domain has no mailbox today: there is no `MX` record, the only SPF record is
`v=spf1 ~all`, and there is no DMARC policy. The host already runs Debian 13 with podman 5.4.2,
rootless quadlets for the app and Prometheus, and one rootful Caddy on the host network. This plan
adds inbound SMTP, authenticated submission, IMAP, and advisory spam filtering to that host, with
SPF, DKIM and DMARC published at Mythic Beasts.

## The short version

- **A pod, not one container.** Three long-running containers — Postfix, Dovecot and Rspamd — plus
  the existing Caddy for certificates. They share the pod's network namespace, so LMTP, the milter
  and Dovecot SASL are plain TCP on `127.0.0.1`; the historic pain of multi-container mail was
  sharing Unix sockets, and a pod removes it.
- **Postfix** takes port 25 (MX) and 587 (submission, SASL + STARTTLS), and hands every accepted
  message to **Dovecot** over LMTP. Dovecot serves IMAP on 993 only. POP3 is not installed and not
  published.
- **Rspamd** is the milter: it evaluates SPF, DKIM and DMARC (and can validate ARC), signs our
  outbound mail, and scores inbound mail. One process replaces OpenDKIM + OpenDMARC +
  pypolicyd-spf + SpamAssassin.
- **Spam filtering is advisory.** Rspamd is configured never to reject, greylist or soft-reject;
  it only adds headers. If Rspamd is down, Postfix accepts mail untagged. Per-account thresholds and
  a per-account decision to keep mail in the INBOX or file it into `Junk` are one small config file
  per address.
- **DNS:** `mail.moosemorals.com` with A, AAAA and matching PTR; `MX`; one SPF record; one DKIM
  selector; DMARC at `p=none` first, then `p=quarantine` and `p=reject`. TLS for SMTP and IMAP comes
  from the Caddy instance that already holds a certificate for the domain.
- **The hard part is not the containers.** It is deliverability: a new IP, a PTR record, port 25,
  and not being mistaken for a spam source. The plan spends as much time there as on the software.

## Goals and non-goals

Goals:

- Receive mail for `moosemorals.com` over SMTP and read it over IMAP.
- Send mail as `moosemorals.com` over authenticated submission, signed with DKIM.
- Publish correct SPF, DKIM and DMARC; evaluate and report inbound authentication results.
- Filter spam without ever losing a message: score and tag, with a per-account threshold and an
  optional `Junk` folder.
- Keep the stack deployable from this repository, with the quadlets as the source of truth, and
  backed up off-host.
- Fit beside the website on one 8 GB CAX21 without threatening the app's 2 GB memory budget.

Non-goals:

- POP3. Not installed, not published, not configurable.
- Webmail, CalDAV/CardDAV, mailing lists, catch-all mailboxes, multi-domain hosting.
- High availability or a second mail host.
- Self-service account management, and integration with the website's editor accounts.
- DANE and MTA-STS for the MVP (both are cheap follow-ups; see "Deliberately not doing").
- Importing existing mail. There are no existing mailboxes to import.
- A web UI for spam tuning. The config file is the MVP.

## What already exists

The plan builds on facts that are already true, not on a blank sheet.

**Host.** Hetzner CAX21: four Arm64 vCPUs, 8 GB RAM, 80 GB SSD, Debian 13 (trixie). Podman 5.4.2
supports pod quadlets. Caddy is rootful on the host network because it binds 80/443; the app and
Prometheus are rootless. `ufw` allows 22, 80 and 443 only. There is a 4 GiB swap file, the app is
capped at 2 GB with swap disabled, and Prometheus keeps up to 4 GB. The disk is shared with the
container store, the rollback image, `storage.db` and those Prometheus metrics.

**Domain.** DNS is served by `ns1`/`ns2.mythic-beasts.com`. `moosemorals.com` has:

| Record | Current value | Consequence |
| --- | --- | --- |
| `A` | `91.98.155.87` | Hetzner `CLOUD-FSN1`; the VM's public IPv4 |
| `AAAA` | `2a01:4f8:c016:b770::1` | Hetzner IPv6 |
| `PTR` for both | `moosemorals.com` | must become `mail.moosemorals.com` |
| `MX` | none | the domain cannot receive mail |
| `TXT` | `v=spf1 ~all` | soft-fails, and there must remain only one SPF record |
| `_dmarc` | none | no policy |
| `CAA` | `issue "letsencrypt.org"` | Caddy can obtain the mail certificate |

**What is missing.** Ports 25, 587 and 993 are closed in `ufw`; nothing listens on them; the
domain has no mail host name; and the host has no mail volumes. This is greenfield: there is no
existing mailbox to migrate and no existing MX to cut over.

> **Do not make the apex the mail host.** The apex already answers for the website, and both PTRs
> currently say `moosemorals.com`. Use a dedicated `mail.moosemorals.com` so the web edge and the
> mail identity can move independently, and point both PTRs at the mail name. The addresses are the
> VM's own (read them from the Hetzner console), so the A/AAAA values happen to match the apex; the
> names and the PTRs must not.

## Decisions

### 1. A pod of separate containers, not one container with a supervisor

The alternatives, honestly:

| | One container (Postfix + Dovecot + filter under s6/supervisord) | Pod of containers |
| --- | --- | --- |
| Units to own | one `.container` | one `.pod` plus three `.container` files |
| Configuration | one image, one directory, one supervisor | one mounted directory per daemon |
| Inter-process paths | Unix sockets in one filesystem, trivially | TCP on the pod's shared loopback, trivially |
| Upgrades | all daemons at once | per container, independent tags |
| Failure | one bad process can take the container with it | healthcheck per daemon, restart per daemon |
| Isolation | filter and mail store share a filesystem | separate filesystems, one network namespace |
| Debugging | one `podman exec` | named containers, `podman pod ps` |
| Image work | one image, but it is yours to curate | one `Containerfile` with three targets |

The last time this was attempted, the difficulty was not the number of daemons: it was making
Postfix find Dovecot's LMTP socket, Dovecot's auth socket and Rspamd's milter socket across
containers. **A pod shares one network namespace**, so every one of those becomes
`127.0.0.1:<port>` and the socket-volume problem disappears. What remains is ordinary config
management, which a single container does not remove — it only hides it inside somebody else's
supervisor.

The pod also keeps the failure modes separate. A crashed Rspamd must not stop Postfix from
accepting mail; a restart of Dovecot must not drop the SMTP listener. That is easier to express with
three healthchecks than with one supervisor.

If the spike in Phase 1 shows that the pod's loopback, user mapping or quadlet support does not
behave, the fallback is the single container: it is a packaging change, not an architectural one.
The Postfix-to-Dovecot-to-Rspamd interfaces are the same either way.

### 2. A rootful pod, like Caddy, for the MVP

The mail pod is a **system** quadlet under `/etc/containers/systemd`, not a user quadlet. The
reasons are concrete:

- **Low ports.** Postfix must bind 25 and 587 and Dovecot 993. Rootful podman binds them directly.
  Rootless podman would need `net.ipv4.ip_unprivileged_port_start=25` on the host and rootlessport
  forwarding on every publish, which is a host-wide change and one more moving part.
- **Certificates and secrets.** The mail containers read the synced certificate and the DKIM key,
  both root-owned `0640` bind mounts. A rootful pod reads them directly; a rootless pod would need
  a mapped group for each, or a copy into a volume. It also cannot read the rootful Caddy volume,
  which is where the sync timer gets its input.
- **Secrets.** The Dovecot password file is a root-owned `0640` bind mount. Rootless would push it
  into a named volume and require `podman exec`/`podman volume` ceremonies to write it.

The cost is real: Postfix, Dovecot and Rspamd parse hostile input and would run without a user
namespace, so a container escape is host root. That is the same bargain Caddy already makes, and it
is mitigated by a read-only root filesystem, `NoNewPrivileges`, a minimal capability set, a
`--pids-limit`, a memory cap, seccomp defaults, and no shared volume with the website. If that
bargain is unacceptable later, the rootless variant is a day's work: a secrets volume, the sysctl,
and either lego with DNS-01 at Mythic Beasts or a root timer that copies Caddy's certificate into a
deploy-owned volume. It is not part of the MVP.

### 3. Postfix for SMTP, Dovecot for IMAP and LMTP, no POP3

Postfix 3.10 is the MTA. It is virtual-mailbox-only: there are no Unix users per mailbox, no local
delivery, and no `mydestination`. Dovecot 2.4 owns the mail store and is the only process that
writes it.

| Port | Protocol | Process | Purpose | Public |
| --- | --- | --- | --- | --- |
| 25 | SMTP | Postfix | inbound MX; no AUTH | yes |
| 587 | SMTP + STARTTLS | Postfix | submission; SASL required | yes |
| 993 | IMAPS | Dovecot | IMAP over TLS | yes |
| 24 | LMTP | Dovecot | Postfix delivery, pod loopback | no |
| 12345 | Dovecot auth | Dovecot | Postfix SASL, pod loopback | no |
| 11332 | milter | Rspamd | Postfix scan/sign, pod loopback | no |
| 11334 | HTTP | Rspamd | controller, `ssh -L` only | host loopback |

Deliberately absent: 110 and 995 (POP3), 143 (plain IMAP), 465 (implicit TLS submission), 4190
(ManageSieve). A client that cannot do 993/587 is a client to revisit, not a reason to open more
ports.

Postfix's `master.cf` keeps only the `smtp`, `submission`, `pickup`, `cleanup`, `qmgr`, `bounce`,
`defer`, `trace`, `verify`, `flush`, `proxymap`, `proxywrite`, `showq`, `error`, `retry`,
`discard`, `local`, `virtual`, `lmtp`, `anvil`, `scache` and `tlsmgr` services. `chroot` is `n`
throughout, because the container is the boundary. On port 25 `smtpd_sasl_auth_enable = no`; on
587 it is `yes` with `smtpd_tls_security_level = encrypt`.

`virtual_transport = lmtp:inet:127.0.0.1:24`. Postfix is the SMTP server and the LMTP **client**;
Dovecot is the LMTP server. Postfix never mounts `/var/mail`, so Dovecot is the only writer to the
mail store. `virtual_mailbox_maps` is a `texthash:` of the addresses that exist, so an unknown
recipient is rejected at `RCPT TO` (550) rather than accepted and bounced. `mynetworks = 127.0.0.0/8` and `reject_unauth_destination` ensure the submission port
is not an open relay. `recipient_delimiter = +` gives `osric+anything@` for free.

### 4. One process for SPF, DKIM, DMARC and spam: Rspamd

**First, the terminology.** SPF and DMARC do not need a server. They are DNS policies that the
*receiving* side enforces; the sending side publishes a TXT record and signs with DKIM. DKIM is the
only one that needs a signer. So "appropriate servers" means:

| Concern | What it actually needs | Traditional daemon | This plan |
| --- | --- | --- | --- |
| SPF | a TXT record, plus a receiver-side check | `pypolicyd-spf` at SMTP time | Rspamd `spf` module |
| DKIM | a private key, a selector record, a signer | OpenDKIM milter | Rspamd `dkim_signing` |
| DMARC | a `_dmarc` record, plus a receiver-side evaluator | OpenDMARC milter | Rspamd `dmarc` module |
| ARC | optional chain validation/sealing | OpenARC | Rspamd `arc` module |
| Spam | a scorer that adds headers | SpamAssassin via `spamass-milter` or amavisd | Rspamd |

Rspamd fits this job better than the four-daemon split because the expensive part — parsing the
message and doing DNS lookups — is shared, and because per-recipient settings are a first-class
feature rather than an SQL schema.

#### SpamAssassin vs Rspamd

SpamAssassin 4.0 is still maintained and is still a good filter. The honest comparison:

| | SpamAssassin 4.0.1 | Rspamd 3.12.1 |
| --- | --- | --- |
| Runtime | Perl; `spamd` is typically 150–250 MB | C; typically 100–200 MB with Hyperscan |
| Throughput | fine at personal volume; per-message Perl work is slower | designed for high volume; much cheaper per message |
| Rule updates | `sa-update`, independent of the package | rules ship with the release; updates mean a new image |
| DKIM/SPF/DMARC/ARC | needs OpenDKIM, pypolicyd-spf, OpenDMARC | built in |
| Milter | `spamass-milter` is old; amavisd-new is the usual glue | native milter |
| Per-user thresholds | `user_prefs`/SQL scores; per-user Bayes needs `spamd --virtual-config-dir` | `settings` blocks matched on `rcpt`/`from`, native |
| Learning | mature per-user Bayes | Bayes/neural via Redis; needs feedback to be useful |
| Familiarity | high | new configuration language |
| Failure modes | one more daemon plus the milter glue | one daemon, fail-open via Postfix |

**Recommendation: Rspamd, for the integrated auth-and-scoring role.** The decisive points are
per-recipient settings (the requirement), one process instead of four, and the fact that the
milter interface is the seam: if Rspamd disappoints, replace it with `spamass-milter` and keep
Postfix, Dovecot, the headers and the per-account files. Do not run both — two scorers double the
CPU and fight over `X-Spam-*` headers.

The other names in this space are not complete replacements: Bogofilter is a Bayesian classifier,
CRM114 is a statistical filter with its own language, and DSPAM is effectively unmaintained. Each
still needs an MTA integration and a rule set, and none brings SPF, DKIM or DMARC with it.

The one genuine loss is `sa-update`: SpamAssassin can take new rules without a package upgrade,
whereas Rspamd rules are part of its release. The fix is operational, not architectural: rebuild
the mail image monthly, which the `image-check`/pinned-digest discipline already encourages.

#### Rspamd's role, precisely

- **Inbound (port 25):** verify SPF, DKIM and DMARC (ARC when a forwarder supplies it); add
  `Authentication-Results`; score; add
  `X-Spam-Status`, `X-Spam-Level`, `X-Spamd-Result`; add `X-Spam-Flag: YES` only when the message
  crosses the recipient's threshold.
- **Outbound (port 587, authenticated):** skip the spam checks, sign with DKIM. Postfix passes
  `milter_macro_daemon_name=ORIGINATING`, and Rspamd's `authenticated` settings block disables RBL
  and spam scoring for that path while `dkim_signing` still runs.
- **Never:** reject, soft-reject, greylist, quarantine at SMTP, or discard. The `reject` and
  `greylist` action thresholds are set to 1000, far above any realistic score.
- **Fail-open:** `milter_default_action = accept` in Postfix. If Rspamd is down or times out, mail
  is accepted untagged and unsigned; scanning resumes when it returns. This is the correct failure
  mode for "advisory only", and it is tested explicitly.

### 5. Advisory only, enforced twice

"Advisory" is not a slogan in the config; it is a property that has to survive a bad rule update, a
crashed filter and a message that scores 200.

The rules:

1. **No content rejection.** `actions.conf` sets `reject = 1000`, `greylist = 1000`,
   `add header = 4.0`. Per-account files repeat `reject` and `greylist` at 1000 so a per-account
   override cannot weaken the global rule; `make mail-check` fails the deploy if any file sets
   either below 1000. A GTUBE test message must be *delivered* with headers, not refused.
2. **No greylisting.** It is a delay, not advice, and it cannot be per-account.
3. **No connection-time DNSBL rejection.** A DNSBL answer at `RCPT`/`DATA` is a global decision
   about an IP, not a per-account judgement about a message. It also fails the "advisory" test.
   Rspamd may still use RBL results as *symbols* in the score; that is advice.
4. **Unknown recipients are still rejected** (550 at `RCPT TO`) and relay is still denied. Those are
   protocol correctness, not spam filtering; accepting mail for addresses that do not exist would
   create backscatter.
5. **Postfix accepts if the filter is unavailable.** `milter_default_action = accept`; Rspamd
   timeouts are logged, not fatal.
6. **Nothing is discarded.** The optional `Junk` folder is a folder. There is no `Trash` action, no
   `/dev/null` transport, and no Sieve `discard`.
7. **No module may soft-reject.** Greylisting and ratelimiting are separate modules that can emit
   `soft reject` even when the action thresholds are high; they need Redis, which the MVP does not
   run, and `greylist_min_score = 1000` keeps them inert when Redis arrives for Bayes. No
   `force_actions` or `multimap` rule is allowed to name `reject`, `soft reject` or `discard`.

#### Headers

`milter_headers.conf`:

```hcl
use = ["authentication-results", "x-spam-status", "x-spam-level",
       "x-spamd-result", "spam-header"];
routines {
  "spam-header" {
    header = "X-Spam-Flag";
    value = "YES";
    remove = 0;
  }
}
extended_spam_headers = false;
skip_authenticated = true;
```

- `Authentication-Results` is the standards-track record of SPF/DKIM/DMARC for the message.
- `X-Spam-Status` is the SpamAssassin-shaped `Yes/No, score=...` line.
- `X-Spam-Level` is stars, for clients that colour by stars.
- `X-Spamd-Result` lists the symbols and scores. It is the tuning aid and the audit trail; it is
  only added to inbound mail.
- `X-Spam-Flag: YES` is the single bit the delivery rule uses. It is not added to authenticated
  (outbound) mail, and a foreign `X-Spam-Flag` is removed before ours is added.

#### Per-account tuning

One file per address, in `deploy/mail/rspamd/settings.d/`, named for the address and mounted into
Rspamd. There is no generator and no database in the MVP; the file is native Rspamd `settings`
syntax, so there is nothing between the operator and the filter.

`deploy/mail/rspamd/settings.d/osric@moosemorals.com.conf`:

```hcl
# osric@moosemorals.com — advisory threshold 4.5, file flagged mail into Junk.
# Set "add header" to 1000.0 to keep everything in the INBOX: the score headers are
# still added, so the account is still advised; it is just never flagged or moved.
"osric@moosemorals.com" {
  rcpt = "osric@moosemorals.com";
  priority = high;
  apply {
    actions {
      reject = 1000.0;
      greylist = 1000.0;
      "add header" = 4.5;
    }
  }
}
```

Two knobs, and that is the MVP:

- `"add header"` — the score at which this mailbox sees `X-Spam-Flag: YES`.
- The presence of a flag plus a global Sieve rule decides INBOX vs `Junk`. An account that wants
  pure advice (never moved) sets `"add header" = 1000.0`; its mail stays in the INBOX with
  `X-Spam-Status` and `X-Spamd-Result` intact.

A single global Sieve script, run before any user script, does the moving:

```sieve
require ["fileinto", "mailbox"];
if header :contains "X-Spam-Flag" "YES" {
  fileinto :create "Junk";
}
```

A message addressed to several local mailboxes is scanned once, by the milter, before it is split
for delivery. Rspamd therefore applies one settings block to the whole message: the first matching
`rcpt` wins, and every recipient's copy gets that threshold. For a personal domain that is a rare
corner; if two addresses genuinely need different thresholds for the same message, the honest fix
is a per-user Sieve rule or a separate message, not a second scan.

Allow/deny lists are phase 2. They fit the same file as additional `settings` blocks matched on
`rcpt` + `from`, but the exact score-adjustment behaviour needs a spike with Rspamd 3.12 before it
is promised. In the meantime a per-account Sieve rule can rescue or file a sender without changing
the score, which is the more honest advisory behaviour.

### 6. Accounts, authentication and storage

**Dovecot passwd-file, not the website's `Users` table.** The editor accounts are for the website's
cookie authentication and are stored in `storage.db` with PBKDF2 hashes. Mail needs a different
credential — ideally an app password that can be revoked without signing the editor out — and
Dovecot needs a userdb it can read over the pod, not a SQLite file owned by another container.
Coupling the two would also mean the mail pod mounts the website's database volume, which is the
one thing the isolation is supposed to prevent.

The MVP is `/etc/dovecot/users`, a root-owned `0640` passwd-file:

```
osric@moosemorals.com:{ARGON2ID}$argon2id$...:5000:5000::/var/mail/osric@moosemorals.com::
```

The hash is produced by `doveadm pw -s ARGON2ID` inside the running Dovecot container. A `mail-user`
Make target prompts with echo off and appends the line, the same pattern as `make user-add`. No
password goes into this repository or into the image. A future version can move the passdb to SQLite
in a mail-owned database, or add app passwords, without changing Postfix.

**Maildir, not mdbox, for the MVP.** Maildir is self-describing, recoverable with `rsync` and
`grep`, and every tool understands it. mdbox is faster and smaller at scale, but this is a personal
domain and the recovery story matters more. Dovecot's `mail_driver = maildir`,
`mail_home = /var/mail/%{user}`, `mail_path = ~/Maildir`.

**Quotas.** Advisory-only filtering plus no discards means a full mailbox is a real risk. Set a
per-account quota (5 GB is a reasonable default) and a 90% warning; over quota, LMTP tempfails and
Postfix queues, which is visible and recoverable. The mail volume is on the same 80 GB disk as the
container store, `storage.db` and Prometheus; monitor `df` and keep the total quota below half the
free space.

**Aliases.** A `virtual` file maps `postmaster@`, `abuse@`, `hostmaster@`, `webmaster@` and
`dmarc@` to the owner, so the RFC 2142 addresses work and DMARC reports have somewhere to land.
No catch-all.

### 7. TLS from Caddy's ACME store

Caddy already obtains and renews certificates with Let's Encrypt, and the CAA record allows it.
Add a Caddy site block for the mail host:

```caddyfile
mail.moosemorals.com {
    respond "mail.moosemorals.com" 200
}
```

Caddy solves HTTP-01 on port 80 and writes the certificate into `caddy-data`. The mail daemons must
not read that volume directly: Caddy writes the private key `0600 root:root`, and Postfix's `smtpd`
runs as `postfix`, so it could not open it. Instead a root `mail-cert-sync` timer copies the leaf
certificate and key into `/etc/mail/tls/` with permissions the daemons can use:

```
/etc/mail/tls/mail.moosemorals.com.crt   0644 root:root
/etc/mail/tls/mail.moosemorals.com.key   0640 root:mail
```

`postfix` and `dovecot` are members of the `mail` group. The copy is a `find` for the leaf files
under Caddy's `certificates` tree, followed by a hash comparison: if the certificate changed, the
timer runs `postfix reload` and `doveadm reload` (both re-read it); otherwise it does nothing. The
mail containers mount `/etc/mail/tls` read-only, so Caddy's storage layout and its ACLs stay out of
the mail configuration.

This is deliberately more machinery than a bind mount, and it is the minimum that works with a
root-only key. It also gives `make mail-check` a stable path to assert and a single place to fix
permissions. If the certificate is missing, the deploy fails before it restarts Postfix rather than
discovering the problem on the first TLS handshake.

This avoids a second ACME client, a Mythic Beasts API key and a DNS-01 credential. The coupling to
Caddy is deliberate: Caddy is already the edge and already the certificate authority client for the
domain. If the mail host ever moves off this box, the fallback is lego with the Mythic Beasts DNS
API, which lego supports.

### 8. DNS at Mythic Beasts

Add, in this order, and check each with `dig` before adding the next:

| Name | Type | Value | Notes |
| --- | --- | --- | --- |
| `mail.moosemorals.com` | `A` | the VM's own IPv4 | read from the Hetzner console |
| `mail.moosemorals.com` | `AAAA` | the VM's IPv6 | optional but preferred |
| both addresses | `PTR` | `mail.moosemorals.com` | set in the Hetzner console/API |
| `moosemorals.com` | `MX` | `10 mail.moosemorals.com.` | only after the host answers on 25 |
| `moosemorals.com` | `TXT` | `v=spf1 mx -all` | replace the existing `v=spf1 ~all`; never add a second SPF record |
| `mail._domainkey.moosemorals.com` | `TXT` | `v=DKIM1; k=rsa; p=...` | 2048-bit RSA, selector `mail` |
| `_dmarc.moosemorals.com` | `TXT` | `v=DMARC1; p=none; rua=mailto:dmarc@moosemorals.com; ruf=mailto:dmarc@moosemorals.com; fo=1` | tighten later |
| `_smtp._tls.moosemorals.com` | `TXT` | `v=TLSRPTv1; rua=mailto:tlsrpt@moosemorals.com` | optional, useful |
| `_mta-sts.moosemorals.com` | `TXT` | `v=STSv1; id=...` | phase 5, with the HTTPS policy file |

**One SPF record.** The existing `v=spf1 ~all` must be *edited*, not supplemented. Two SPF records
are a permanent error and will break the domain's mail.

**No CNAME at the mail host.** `mail.moosemorals.com` must be an address record; a CNAME would
break both MX resolution and PTR alignment.

**DMARC ramp.** Publish `p=none` with `rua`/`ruf` for at least two weeks, read the aggregate
reports, fix any legitimate source that fails alignment, then move to `p=quarantine`, then
`p=reject`. Only after outbound is consistently signed and aligned should SPF move from `~all` to
`-all`; the two changes are independent and should not land on the same day.

**DKIM key.** Generate with `rspamadm dkim_keygen -s mail -d moosemorals.com -b 2048 -k
/etc/mail/dkim/moosemorals.com.mail.key`; the command prints the TXT record. The filename matches
Rspamd's `$domain.$selector.key` template. Keep the private key
out of the repository, root-owned `0640` and readable by the `_rspamd` group. Plan the rotation
before the key is a year old: publish `mail-2027._domainkey`, switch the selector, wait for the old
TTL, then remove the old record.

### 9. Deliverability and the port 25 question

This is the part that decides whether the project succeeds, and it is mostly outside the container
configuration.

- **PTR before MX.** A receiving MTA that cannot reverse-resolve `mail.moosemorals.com` to the
  sending IP will treat the first message with suspicion regardless of SPF and DKIM.
- **Outbound port 25.** Hetzner has historically blocked outbound 25 on new Cloud projects until it
  is enabled, and the policy has changed over time. Test it *from the host* before publishing MX:
  `swaks --to <a real address> --server <a real MX>` or `nc -vz <mx> 25`. If it is blocked, request
  the unblock in the Hetzner console, and in the meantime use an authenticated smarthost on 587.
  Do not discover this after telling people the address exists.
- **Inbound port 25** is not the same question: `ufw allow 25/tcp` and Hetzner's firewall (if one is
  enabled for the project) must both
  pass it. Test from an external network, not from the host.
- **IPv4 first.** Publish AAAA, but set `smtp_address_preference = ipv4` initially. Some receivers
  treat IPv6 senders more harshly, and a new IPv6 reputation is harder to read than IPv4. Revisit
  once DMARC reports are clean.
- **Warm-up.** Start with personal mail, not a mailing list. Send to Gmail, Outlook and a
  `mail-tester.com` address, read the `Authentication-Results` headers, and check the domain
  against the major blocklists before inviting anyone else to use the address.
- **No bulk mail.** This host should not become a newsletter sender. If that is ever wanted, use a
  relay with its own reputation.

### 10. Backups

Mail is the only data on this host that cannot be regenerated. The website's pages are in
`storage.db`, which already has a backup procedure; mail needs its own.

- **What:** the Dovecot mail volume and the account/passwd file, plus the Rspamd DKIM key. The
  Postfix queue is not worth backing up; a restore that loses a queue is a retry, not a loss.
- **How:** stop the pod, archive the volumes, start the pod — or use `doveadm backup` for an online
  copy. Off-host and encrypted (restic or `age` + `rsync`); the volume lives on the same 80 GB disk
  as everything else, so a copy on that disk is not a backup.
- **Restore test:** restore into a scratch volume and `doveadm force-resync -u <address>`; open the
  mailbox over IMAP. A backup that has never been restored is a hope.
- **Cadence:** daily incremental, weekly full, 30 days retained. Mailbox data is personal; keep the
  encryption key off the host.

The mail volumes must never be shared with the website's volumes. A mail compromise that can read
`storage.db` is a website compromise too.

### 11. Monitoring, logs and health

- Every container logs to journald: `journalctl -u mail-postfix`, `-u mail-dovecot`,
  `-u mail-rspamd`. Postfix's `maillog` goes to stdout; do not write log files inside containers.
- Healthchecks: Postfix `postfix status` (or a TCP probe on 25), Dovecot a probe on 993 plus
  `doveadm service status`, Rspamd `rspamadm control ping` or `rspamc stat`.
- A queue alert is more useful than a CPU alert: `postqueue -p | tail -1` non-empty for more than
  an hour, or more than 50 messages, means delivery is failing. Prometheus exporters for Postfix,
  Dovecot and Rspamd exist; they are a phase 4 nicety, not an MVP dependency.
- Dovecot's `auth_failure_delay = 2 secs` slows credential stuffing on 993/587. If logs show a
  sustained attack, add fail2ban reading the journal; do not lower the port exposure to compensate.
- The website's `/status` page should eventually show queue depth and last delivery, but that is a
  Web change and out of scope here.

### 12. Deployment shape

Everything lives under `deploy/mail/`, in the same style as the existing quadlets:

```
deploy/mail/
  Containerfile              # three targets: postfix, dovecot, rspamd
  mail.pod                   # pod, published ports
  postfix.container
  dovecot.container
  rspamd.container
  cert-sync.sh               # root; copies Caddy's leaf cert into /etc/mail/tls and reloads
  mail-cert-sync.timer
  postfix/
    main.cf
    master.cf
    vmailbox
    virtual
  dovecot/
    dovecot.conf
    users                    # not in git; seeded once, 0640
    sieve/before.sieve
  rspamd/
    local.d/*.conf
    settings.d/<address>.conf
  dkim/                      # not in git; moosemorals.com.mail.key, 0640 root:_rspamd
```

The images are built from `debian:13-slim` pinned by digest, with exact package versions and
`--timestamp` from the commit, matching the existing image discipline. A mirror is not a snapshot,
so a rebuild after a Debian point release can still differ; if bit-reproducibility matters, point
apt at `snapshot.debian.org` with a fixed timestamp. Debian's `postfix` package
needs a debconf preseed (`postfix postfix/main_mailer_type select No configuration`) so the image
build does not prompt, and the runtime config replaces `/etc/postfix` wholesale. The image creates a
`mail` group and adds `postfix` and `dovecot` to it, so the synced `0640` key is readable. Dovecot
installs `dovecot-core dovecot-imapd dovecot-lmtpd dovecot-sieve` — deliberately **not**
`dovecot-pop3d`.
Rspamd installs `rspamd` only; Redis is not needed until Bayes or rate limiting is enabled, and
while it is absent the greylisting and ratelimit modules have no cache to act on. `mail-check` still
greps the shipped config for a lower `reject`/`greylist`, a `soft reject` or a `discard`, so adding
Redis in phase 5 cannot quietly turn advice into refusal.

Make targets, following the existing naming:

| Target | Effect |
| --- | --- |
| `make mail-image` | build the three images and tag them with `GIT_SHA` |
| `make mail-deploy HOST=...` | push, install the pod and configs, `daemon-reload`, restart, wait for health |
| `make mail-check HOST=...` | `postfix check`, `doveconf -n`, `rspamadm configtest`, and the advisory-threshold assertions |
| `make mail-user-add ADDRESS=...` | prompt for a password, `doveadm pw`, append to `users`, update `vmailbox` |
| `make mail-dkim` | generate the key and print the DNS record; never overwrite an existing key |
| `make mail-logs` / `mail-status` | journal tail / `podman pod ps` and per-container health |
| `make mail-backup` | online `doveadm backup` to a tarball, for the off-host copy |

`mail-deploy` must be a gate, not a hope: validate every config before restarting, check that the
mail host's A/AAAA and PTR agree with the host, and do not report success until every container is
healthy. It should be safe to run on its own, like `make deploy`, and it must not touch the
website's units.

The container and daemon excerpts are in [the configuration sketches](email-configuration.md), so
this plan can stay the decisions and the order to do them in.

## Phases

### Phase 0 — pre-flight (half a day)

- Read the VM's own IPv4 and IPv6 from the Hetzner console; do not infer them from the apex A.
- Test outbound 25 from the host; request the Hetzner unblock if needed.
- Confirm `dig` shows one SPF record and no MX; record the current values for rollback.
- Check free disk and decide the mail quota total.
- Spike: a two-container pod where one container reaches the other's `127.0.0.1` listener, and a
  quadlet `.pod` starts on this host. This is the only assumption in the plan worth testing before
  writing config.

### Phase 1 — images, pod, Postfix and Dovecot (1–2 days)

- Build the three images; `postfix check`, `doveconf -n`, `rspamadm configtest` all pass.
- Postfix accepts on 25 and 587; Dovecot authenticates from the passwd-file and serves 993.
- Delivery via LMTP into Maildir; `swaks` from another host; read the message over IMAP.
- No spam filtering, no DKIM yet. This is a working "receive and read" stack.
- Prove no POP3: `ss -ltn` shows no 110/995, and `doveconf -n protocols` shows imap and lmtp only.

### Phase 2 — Rspamd, auth and advisory headers (1 day)

- Milter connected; `Authentication-Results` present; DKIM signing works for submission.
- Add the `mail.moosemorals.com` A/AAAA records (the certificate challenge needs them), publish the
  DKIM record and the Caddy site block, and get the certificate.
- GTUBE test: accepted, scored, tagged, never rejected.
- Stop Rspamd: mail still arrives, untagged, and the queue drains when it returns.
- Per-account file: one account at the default threshold, one at `1000` (INBOX only); verify both.

### Phase 3 — DNS and first real mail (half a day, then soak)

- Add PTR, MX, SPF, DMARC `p=none`, TLS-RPT.
- Send from an external account; read the headers; check `mail-tester.com` and DMARC reports.
- `swaks` submission to Gmail and Outlook; confirm DKIM and DMARC pass.
- Open 25/587/993 in `ufw`; verify from outside the host. Add the same rules to
  `deploy/cloud-init.yaml` so a rebuilt host is not a surprise, and update `deploy/README.md` with
  the mail volumes, the certificate coupling and the backup procedure.

### Phase 4 — hardening and operations (1 day)

- Quotas, backups, the restore test, the cert-sync timer, queue monitoring.
- DMARC `p=quarantine` once reports are clean; then `p=reject`; SPF `-all`.
- `auth_failure_delay`; review logs for brute force; decide on fail2ban.

### Phase 5 — optional follow-ups

- Bayes learning from `Junk`/INBOX moves via `imapsieve` (needs Redis).
- Allow/deny lists per account in the same `settings.d` file.
- MTA-STS (Caddy can serve the policy file) and DANE if the zone is signed.
- Webmail (SnappyMail or Roundcube) as a fourth container, behind Caddy.
- Mailbox management in the website's `/admin`, backed by a mail-owned SQLite database.

## Test matrix

| Test | Expected |
| --- | --- |
| `swaks --server mail.moosemorals.com --port 25 --to osric@...` | 250, delivered to Maildir |
| Same, unknown recipient | 550 at RCPT, no queue entry |
| `swaks --port 587` with SASL + STARTTLS | accepted; DKIM signature on arrival |
| `swaks --port 587` without SASL | 5xx, no relay |
| `openssl s_client -connect mail...:993` | certificate for `mail.moosemorals.com`, IMAP banner |
| `doveconf -n protocols` / `ss -ltn` | imap + lmtp only; no 110/995 |
| GTUBE to a `junk` account | delivered, `X-Spam-Flag: YES`, in `Junk` |
| GTUBE to an `inbox` account | delivered to INBOX, score headers, no flag |
| `podman stop mail-rspamd`, then send | delivered, untagged; accepted, not deferred |
| `rspamc < message.eml` | action is `add header` or `no action`, never `reject` |
| DKIM check at `check-auth@verifier.port25.com` | `dkim=pass`, `spf=pass`, `dmarc=pass` |
| `dig TXT moosemorals.com` | exactly one SPF record |
| `dig -x <mail IPv4>` | `mail.moosemorals.com` |
| Restore a mailbox backup into a scratch volume | IMAP lists it; `doveadm force-resync` clean |

## Risks

- **Deliverability is the project risk.** A new IP with no history, an unset PTR, or a blocked
  outbound 25 can make every message fail even when the stack is perfect. Phases 0 and 3 exist for
  this, and the DNS/PTR work comes before the MX.
- **Advisory-only means volume.** Spam is accepted and stored (or filed); quotas and monitoring are
  not optional. The upside is that nothing legitimate is ever refused by a score.
- **Rootful containers.** Postfix, Dovecot and Rspamd parse untrusted input without a user
  namespace. If that is unacceptable, the rootless variant described in Decision 2 costs a secrets volume,
  the port sysctl and a certificate copy.
- **Dovecot 2.4 config drift.** Most tutorials are 2.3. Use `doveconf -n` and the 2.4 docs, not
  blog posts.
- **Certificate sync.** The sync timer knows both Caddy's storage layout and the mail group's gid.
  If either moves, mail TLS breaks at the next renewal; `make mail-check` asserts that the synced
  files exist, match the certificate Caddy serves, and are readable by the daemons.
- **Single host, no HA.** Disk or host loss means mail down and a restore from backup. The website
  has the same property; mail is harder because senders retry for days, not seconds.
- **Account provisioning is manual.** Adding a mailbox is a commit plus `make mail-user-add` plus a
  deploy. That is acceptable for a personal domain and is the price of not putting credentials in
  git.
- **Rspamd rules move with the image.** A monthly rebuild is part of operations; skipping it slowly
  ages the filter.

## Deliberately not doing

- **POP3.** No package, no listener, no option. IMAP only, as requested.
- **Greylisting, DNSBL rejects, postscreen rejects, SMTP-time quarantine.** They are global,
  connection-level decisions and cannot be advisory or per-account.
- **SpamAssassin in the MVP.** It is the fallback behind the same milter seam, not a second scorer.
- **A single do-everything container.** It is the documented fallback, not the plan.
- **Rootless mail containers in the MVP.** The security argument is real, but the port, secret and
  certificate work is a day of yak-shaving that does not improve the mail.
- **Webmail, CalDAV/CardDAV, mailing lists, catch-all.** Different products.
- **Reusing the website's `Users` table.** Mail credentials and editor sessions are separate
  authorities.
- **DANE and MTA-STS in the MVP.** MTA-STS is a later Caddy file and a TXT record; DANE needs a
  signed zone and a validating resolver.
- **A web UI for spam tuning.** The per-account file is the interface until the file is painful.

## Validation

The plan is done when all of these are true on the deployed host, not just in a local container:

1. `make mail-check HOST=...` passes: `postfix check`, `doveconf -n`, `rspamadm configtest`; no
   warnings about missing queue directories, certificates or passdb files.
2. From a network that is not the host: `swaks` delivers to a real mailbox on 25, and `openssl
   s_client` completes an IMAP login on 993 with a valid certificate for `mail.moosemorals.com`.
3. An unknown recipient gets 550; submission without SASL gets 5xx; submission with SASL delivers
   and the received message has a DKIM signature that verifies at `check-auth@verifier.port25.com`.
4. A GTUBE message is **delivered**: it appears in the account's INBOX or `Junk` according to its
   `settings.d` file, with `X-Spam-Status`, `X-Spam-Level` and (for a `junk` account)
   `X-Spam-Flag: YES`.
5. With `mail-rspamd` stopped, a message still arrives; `journalctl -u mail-postfix` records the
   milter failure and Postfix's `milter_default_action` accepts it.
6. `dig MX`, `dig TXT` (one SPF), `dig TXT mail._domainkey`, `dig TXT _dmarc`, and `dig -x` for
   both addresses all return the intended values; `mail-tester.com` reports SPF, DKIM and DMARC
   pass.
7. A mailbox backup restores into a scratch volume and opens over IMAP; the restored message count
   matches the source.
8. `podman pod ps` shows the pod running; each container's healthcheck is healthy; `ufw status`
   allows 25, 587 and 993 and nothing else new.
9. No process listens on 110, 143, 465 or 995 on the host.
10. The website still deploys and answers `/heath` after the mail pod is added: the mail work has
    not touched the app's units, network or volumes.

## Open questions

- **Rootful or rootless?** The plan recommends rootful for the MVP. If the threat model says a
  network-facing parser must not be rootful, decide that before Phase 1; it changes the secret and
  certificate plumbing, not the daemons.
- **One mailbox or several?** The per-account files scale, but the `/etc/dovecot/users` and
  `vmailbox` provisioning is manual. If this is for more than a handful of people, a small SQLite
  passdb and a `mail-user` command should come earlier.
- **Maildir or mdbox?** Maildir is the MVP; if the volume grows past a few tens of GB, mdbox is
  worth a benchmark, not a guess.
- **Direct-to-MX or a smarthost?** Depends on the Hetzner port 25 answer. A smarthost on 587 is
  easier to start and worse for long-term independence.
- **Where do account definitions live?** Non-secret tuning is in git; the password hash is on the
  host. Is a commit per mailbox acceptable, or should the whole `accounts/` tree be host-side and
  only templated from git?
- **What is the retention/quota target?** 5 GB per mailbox is a placeholder; it should follow from
  the disk budget and the number of people.
- **Do we ever want a webmail?** It changes the TLS and Caddy story (another public name) and the
  threat model. Not now, but decide before the account model is frozen.
