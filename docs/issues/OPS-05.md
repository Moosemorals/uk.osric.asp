<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# OPS-05 — No outbound notifications when the site breaks

| | |
| --- | --- |
| Severity | Medium |
| Area | Operations, deployment |
| Location | `deploy/`, `src/uk.osric.asp.Web` |

## Problem

Nothing tells anyone when the site is down. The quadlets restart a crashing app up to three times and
then give up (see OPS-02), and `Restart=always` is otherwise silent: a failed unit, a full disk, a
startup migration that stops the app, or Caddy losing its certificate all sit unnoticed until someone
happens to look. There is no outbound mail, so systemd's failure notifications have nowhere to go.

## Fix outline

1. Pick an outbound mail provider (any SMTP relay that authenticates) and a sender address on the
   site's own domain, e.g. `alerts@<domain>`.
2. Publish the DNS so alerts are not dropped as spam:
   - `SPF` (`TXT @ v=spf1 include:<provider> ~all`) authorising the provider;
   - `DKIM` (the provider's selector key, published as a `TXT` record);
   - `DMARC` (`TXT _dmarc v=DMARC1; p=quarantine; rua=mailto:...`) so failures are reported.
3. Install a minimal relay MTA on the host (`msmtp` or `nullmailer`) with the provider credentials in
   a root-only file; nothing in the app itself needs to send mail.
4. Point systemd at it:
   - `OnFailure=` (and `OnUnitInactiveSec=`) on the `uk.osric.asp` user unit and the `caddy` system
     unit;
   - a small timer that curls `/heath` and alerts when it returns 503, tying the signal from OPS-02
     to a human.
5. Keep credentials out of the repository: ship the MTA configuration through cloud-init and
   `make deploy`, exactly as the quadlets are shipped today.
