<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Email configuration sketches

The load-bearing excerpts for [the email plan](email.md), kept apart so the plan reads as decisions.
Nothing here is deployed, and the configuration is not complete: it is the parts the plan argues
about, in the shape the implementation is tested against. Decision numbers refer to the plan.

## Pod

```ini
# deploy/mail/mail.pod — a user quadlet, under `vmail` (plan, Decision 2)
[Pod]
PodName=mail
PublishPort=25:25
PublishPort=587:587
PublishPort=993:993
# The host's own mail, on the host's loopback only: no SASL, no TLS (Decision 6).
PublishPort=127.0.0.1:2525:2525
# The Rspamd UI, for `ssh -L 11334:127.0.0.1:11334`. Set a controller password first.
PublishPort=127.0.0.1:11334:11334
# Podman turns host-loopback mapping off by default (`-t none -T none --no-map-gw`); this puts it
# back, so the containers can reach the site at 169.254.42.42:8080. The address is a literal because
# 127.0.0.1 inside the pod is the pod's own loopback (spike: mail-stack-preflight).
Network=pasta:--map-host-loopback=169.254.42.42
```

```ini
# deploy/mail/postfix.container (excerpt)
[Container]
Pod=mail.pod
ContainerName=mail-postfix
Image=registry.osric.uk/mail-postfix:latest
Volume=/home/vmail/mail/postfix:/etc/postfix:ro
Volume=mail-spool:/var/spool/postfix
Volume=mail-postfix-data:/var/lib/postfix
Volume=/home/vmail/mail/tls:/etc/mail/tls:ro
ReadOnly=true
Tmpfs=/tmp
Tmpfs=/run
NoNewPrivileges=true
PodmanArgs=--memory=512m --pids-limit=256
Exec=postfix start-fg
```

Dovecot gets `mail-data:/var/mail`, `mail-dovecot-state:/var/lib/dovecot`, its config, the Lua script
and the Sieve directory, plus the same read-only certificate mount. Rspamd gets its config,
`mail-rspamd:/var/lib/rspamd` — which holds the Bayes statfiles — and `/home/vmail/mail/dkim`. The map listener gets a volume of its own
and shares a `map-socket` volume with Postfix. It has no state volume, because it has no state:

```ini
# deploy/mail/maps.container (excerpt)
[Container]
Pod=mail.pod
ContainerName=mail-maps
Image=registry.osric.uk/mail-maps:latest
Volume=map-socket:/run/maps
Volume=/home/vmail/mail/maps.conf:/etc/maps.conf:ro
Volume=/home/vmail/mail/service.token:/run/secrets/service.token:ro
ReadOnly=true
Tmpfs=/tmp
NoNewPrivileges=true
PodmanArgs=--memory=128m --pids-limit=64
Exec=/mail-maps --config /etc/maps.conf
```

```ini
# deploy/mail/maps.conf — a gateway, not a cache: nothing here survives a restart
socket = /run/maps/socketmap
lookup_url = http://127.0.0.1:8080/internal/mail/lookup   # pasta maps the host's loopback in
token_file = /run/secrets/service.token
timeout_seconds = 5
```
 `[Install]
WantedBy=default.target` goes on the pod and on each container. The entrypoint seeds state on an empty
volume: `postfix check` for the queue, `/var/mail` and `/var/lib/rspamd/bayes` owned by `vmail` for Dovecot
and the classifier, and `recipients.map` for the `per_user` extractor. The Bayes directory is not
optional: the classifier refuses to start without it.

## Host

```sh
# deploy/host/vmail-user.sh — idempotent; run by mail-deploy and cloud-init
useradd --create-home --shell /usr/sbin/nologin vmail
usermod --add-subuids 100000-165535 --add-subgids 100000-165535 vmail
loginctl enable-linger vmail
```

```ini
# deploy/host/25-unprivileged-ports.conf — installed as /etc/sysctl.d/25-unprivileged-ports.conf
# pasta binds 25, 587 and 993 in the host's namespace as vmail, which needs the sysctl to allow it.
net.ipv4.ip_unprivileged_port_start=25
```

```ini
# deploy/host/msmtprc — installed as /etc/msmtprc, 0600 root:root
defaults
auth         off
tls          off
host         127.0.0.1
port         2525
# A bare `root` is qualified without relying on the host's canonical name, which is
# `neith` and not a domain anyone can resolve.
maildomain   neith.moosemorals.com
# Machine mail says what it is, so a Sieve rule can file it (Decision 7, aliases).
from         alerts@moosemorals.com
```

```ini
# deploy/host/mailname — installed as /etc/mailname
neith.moosemorals.com
```

The host also gets `msmtp` and `msmtp-mta` from Debian, so `/usr/sbin/sendmail` is msmtp's. Nothing
in `/etc/msmtprc` is a secret: the listener is loopback-only and asks for no credential.

## Postfix

```ini
# deploy/mail/postfix/main.cf (excerpt)
myhostname = neith.moosemorals.com
mydomain = moosemorals.com
# The machine's mail is root@neith.moosemorals.com, not root@moosemorals.com: the bare
# domain is for people, and the name in HELO, the PTR and the From are the same.
myorigin = $myhostname
mydestination =
local_recipient_maps =
inet_interfaces = all
inet_protocols = all
compatibility_level = 3.6

virtual_mailbox_domains = moosemorals.com
# Served by the socketmap listener in the pod (Decision 8): one UNIX socket, two map names,
# answers from memory. No postmap, no files, no reload.
virtual_mailbox_maps = socketmap:unix:/run/maps/socketmap:vmailbox
virtual_alias_maps = socketmap:unix:/run/maps/socketmap:virtual
# Alias-only: recipients at the machine's name resolve; nobody has a mailbox there.
virtual_alias_domains = neith.moosemorals.com
virtual_transport = lmtp:inet:127.0.0.1:24

# osric+anything@ resolves with no alias line, and the detail survives to Sieve (Decision 5).
recipient_delimiter = +
# Default written out: an alias keeps the extension — shopping+order@ -> osric+order@.
propagate_unmatched_extensions = canonical, virtual

smtpd_banner = $myhostname ESMTP
maillog_file = /dev/stdout
smtpd_helo_required = yes
mynetworks = 127.0.0.0/8
smtpd_relay_restrictions = permit_mynetworks permit_sasl_authenticated reject_unauth_destination
smtpd_recipient_restrictions = permit_mynetworks permit_sasl_authenticated reject_unauth_destination reject_unlisted_recipient

smtpd_sasl_type = dovecot
smtpd_sasl_path = inet:127.0.0.1:12345
smtpd_sasl_security_options = noanonymous
smtpd_sasl_auth_enable = no

smtpd_tls_cert_file = /etc/mail/tls/neith.moosemorals.com.crt
smtpd_tls_key_file  = /etc/mail/tls/neith.moosemorals.com.key
smtpd_tls_security_level = may
smtp_tls_security_level = may
smtp_tls_loglevel = 1
smtp_address_preference = ipv4

smtpd_milters = inet:127.0.0.1:11332
milter_default_action = accept
milter_protocol = 6
non_smtpd_milters =

message_size_limit = 52428800
mailbox_size_limit = 0
```

Nothing here is a rendered file: `virtual` and `vmailbox` are map names the listener answers.

```ini
# deploy/mail/postfix/master.cf (excerpt)

# 587: submission. SASL on, encryption required, and Rspamd treats it as originating.
submission inet  n  -  n  -  -  smtpd
  -o syslog_name=postfix/submission
  -o smtpd_tls_security_level=encrypt
  -o smtpd_sasl_auth_enable=yes
  -o smtpd_client_restrictions=permit_sasl_authenticated,reject
  -o milter_macro_daemon_name=ORIGINATING

# 2525: the host's own mail, published at 127.0.0.1:2525. No SASL, no TLS, trusted by address.
2525      inet  n  -  n  -  -  smtpd
  -o syslog_name=postfix/local-submission
  -o smtpd_sasl_auth_enable=no
  -o smtpd_tls_security_level=none
  # Not 10.88.0.1 for certain: read the address the publication arrives from in Phase 0
  # and pin it. Without it, 25 accepts and 2525 rejects.
  -o mynetworks=127.0.0.0/8,10.88.0.1/32
  -o smtpd_relay_restrictions=permit_mynetworks,reject
  -o smtpd_recipient_restrictions=permit_mynetworks,reject_unauth_destination,reject_unlisted_recipient
  -o smtpd_milters=inet:127.0.0.1:11332
  -o milter_macro_daemon_name=ORIGINATING
  # Header qualification belongs to cleanup, so give this listener its own.
  -o cleanup_service_name=cleanup_local

cleanup_local unix  n  -  n  -  0  cleanup
  -o local_header_rewrite_clients=permit_mynetworks
```

The `smtp` service keeps SASL off. Every other service keeps `chroot` off: the container is the
boundary.

## Dovecot (2.4 syntax)

```ini
# deploy/mail/dovecot/dovecot.conf (excerpt)
dovecot_config_version = 2.4.0
dovecot_storage_version = 2.4.0

protocols {
  imap = yes
  lmtp = yes
}

mail_driver = maildir
mail_home = /var/mail/%{user}
mail_path = ~/Maildir
mail_uid = vmail
mail_gid = vmail
first_valid_uid = 5000

namespace inbox {
  inbox = yes
  separator = /
  mailbox Junk { auto = subscribe }
  mailbox Trash { auto = subscribe }
  mailbox Sent { auto = subscribe }
  mailbox Drafts { auto = subscribe }
}

# Same character as Postfix: osric+anything@ resolves to the osric@ user, and the detail
# stays on the envelope for Sieve.
recipient_delimiter = +

# Passwords are the site's: the Lua script presents them as HTTP Basic and returns its verdict.
passdb lua {
  lua_file = /etc/dovecot/auth-http.lua
}

# The userdb is local on purpose: LMTP has no passdb, so asking the site here would put the
# website in the delivery path and stop the queue draining during an outage.
userdb static {
  userdb_static_allow_all_users = yes
  # 2.4 puts the values in userdb_fields; `uid = …` here is the 2.3 spelling and is refused.
  userdb_fields {
    uid = vmail
    gid = vmail
    home = /var/mail/%{user}
    mail = maildir:~/Maildir
  }
}

service imap-login {
  inet_listener imaps {
    port = 993
    ssl = yes
  }
}
service lmtp {
  inet_listener lmtp {
    address = 127.0.0.1
    port = 24
  }
}
service auth {
  inet_listener auth {
    address = 127.0.0.1
    port = 12345
  }
}

log_path = /dev/stdout
ssl = yes
ssl_server_cert_file = /etc/mail/tls/neith.moosemorals.com.crt
ssl_server_key_file  = /etc/mail/tls/neith.moosemorals.com.key
ssl_min_protocol = TLSv1.2
auth_allow_cleartext = no
# 2.4's variable syntax; the default. The domain is the passdb's business: the Lua script returns the
# account's user name, so what was presented and what is used downstream are separate.
auth_username_format = %{user | lower}
auth_mechanisms = plain login
auth_failure_delay = 2 secs

mail_plugins = quota sieve imap_sieve
quota = maildir:User quota
# Uniform for now, so delivery needs no I/O. Per-account quotas would need a value that survives
# an outage (the auth cache, or a local copy), not a live call on the delivery path.
quota_rule = *:storage=5G
sieve_script before {
  type = before
  path = /etc/dovecot/sieve/before.sieve
}

# Teaching Bayes is a folder move, not a header: into Junk, and back out of it. 2.4 folds these
# settings into an `imapsieve_from` filter-array block; `doveconf -n` decides the spelling.
sieve_plugins = sieve_imapsieve sieve_extprograms
sieve_pipe_bin_dir = /etc/dovecot/sieve
imapsieve_mailbox1_name = Junk
imapsieve_mailbox1_causes = COPY
imapsieve_mailbox1_before = file:/etc/dovecot/sieve/learn-spam.sieve
imapsieve_mailbox2_name = *
imapsieve_mailbox2_from = Junk
imapsieve_mailbox2_causes = COPY
imapsieve_mailbox2_before = file:/etc/dovecot/sieve/learn-ham.sieve
```

```sieve
# deploy/mail/dovecot/sieve/learn-spam.sieve — into Junk is spam
require ["vnd.dovecot.pipe", "copy", "environment", "variables"];
pipe :copy "learn.sh" ["spam", "${imap.user}"];
```

```sieve
# deploy/mail/dovecot/sieve/learn-ham.sieve — out of Junk is ham, and Junk to Junk is neither
require ["vnd.dovecot.pipe", "copy", "environment", "variables"];
if not environment :is "imap.mailbox" "Junk" {
  pipe :copy "learn.sh" ["ham", "${imap.user}"];
}
```

```sh
#!/bin/sh
# deploy/mail/dovecot/sieve/learn.sh — $1 is the class, $2 the account; the message is on stdin.
# -d is the account, so the extractor sees a key it can resolve to itself.
exec rspamc --connect 127.0.0.1:11334 -d "$2" "learn_$1"
```

No `pop3` protocol, no `dovecot-pop3d`, no 110/995. `mail_home` and the static userdb say the same
thing on purpose: the userdb is what LMTP reads, and `mail_home` is the default any other auth path
would fall back to, so neither can surprise the other. `sieve_script before` is 2.4's replacement for
`sieve_before`; confirm the block with `doveconf -n`. The config mount is read-only, so the compiled
`.svbin` needs a writable `bin_path` or a tmpfs at the Sieve directory.

## Rspamd

```hcl
# local.d/actions.conf — never reject, never greylist (Decision 9)
reject = 1000.0;
greylist = 1000.0;
add_header = 4.5;
```

```hcl
# local.d/dkim_signing.conf — one key per sending name (Decision 12)
selector = "mail";
path = "/etc/mail/dkim/$domain.$selector.key";
sign_authenticated = true;
sign_local = true;
use_domain = "header";
allow_hdrfrom_mismatch = false;
```

```hcl
# local.d/worker-proxy.inc
bind_socket = "127.0.0.1:11332";
milter = yes;
```

```hcl
# statistic.conf — the classifier, replacing the shipped file wholesale (Decision 9). Not
# local.d/classifier-bayes.conf: the shipped file warns against using both locals together.
classifier "bayes" {
  backend = "sqlite3";        # one of mmap, sqlite3, cdb (read-only), redis; redis is only the default
  tokenizer { name = "osb"; }
  cache { }                    # defaults to the backend, so the learn cache is sqlite too
  new_schema = true;
  store_tokens = false;
  min_tokens = 11;
  min_learns = 200;            # says nothing at all until both classes have this many
  # One corpus per account (Decision 9). The string form of per_user is compiled as a Lua function
  # called with the task; what it returns is the key. Left off or boolean, Rspamd keys on the
  # principal recipient — the address as sent — so every alias would be its own corpus.
  # add_map, not get_maps()['NAME']: get_maps returns an array, so a name-keyed lookup is nil and the
  # extractor would silently fall back to the recipient (spike: mail-stack-preflight).
  per_user = <<EOD
local rcpt_map = rspamd_config:add_map{
  type = 'map',
  url = 'file:///var/lib/rspamd/maps/recipients.map',
  description = 'recipient to account'
}
return function(task)
  local rcpt = task:get_principal_recipient()
  if not rcpt then return 'unknown' end
  local bare = rcpt:gsub('%+.*@', '@')          -- a plus detail is never an alias: drop it first
  local account = rcpt_map:get_key(bare) or bare
  return (account:match('^[^@]+'))              -- the key is the Dovecot user, so no domain travels
end
EOD
  statfile { symbol = "BAYES_HAM";  spam = false; path = "/var/lib/rspamd/bayes/ham.sqlite"; }
  statfile { symbol = "BAYES_SPAM"; spam = true;  path = "/var/lib/rspamd/bayes/spam.sqlite"; }
  learn_condition = "return require('lua_bayes_learn').can_learn";
}
```

```hcl
# local.d/statistics_group.conf — what a Bayesian verdict is worth, and nothing more
BAYES_SPAM { score = 4.0; description = "Bayesian spam"; }
BAYES_HAM  { score = -4.0; description = "Bayesian ham"; }
```

```hcl
# local.d/settings.conf — the deploy concatenates settings.d/*.conf into this file

# Per-recipient settings come from the site, keyed on the SMTP recipient. Phase 0 proves the
# selector can name it; a spike decides whether a rendered fallback is needed at all.
"site settings" {
  priority = high;
  external_map = {
    map = { external = true; backend = "http://169.254.42.42:8080/internal/mail/settings";
            method = "body"; encode = "json"; timeout = 2.0; };
    # The recipient reaches the site as {"rcpt":["shopping@moosemorals.com"]} — an array even for one.
    # `rcpt` is not a selector name and `to('smtp')` yields a table; this pair is the shape that works
    # (spike: mail-stack-preflight).
    selector = "id('rcpt');rcpts('smtp'):addr";
  }
}

# 587 submits as an authenticated user; scoring is pointless and signing is not.
authenticated {
  priority = high;
  authenticated = yes;
  apply { groups_disabled = ["rbl", "spf", "surbl"]; }
}

# 2525 cannot authenticate, so it is trusted by the host's own address: pasta forwards the published
# port and preserves the source, so that is what arrives (spike: mail-stack-preflight).
"host submission" {
  priority = high;
  ip = "<host-address>/32";   # the host's own address, written by mail-deploy
  apply { groups_disabled = ["rbl", "spf", "surbl"]; }
}

# The floor for every address whose account has no settings (Decision 10). Backslashes are
# doubled because UCL parses the string before the regex sees it.
"default" {
  rcpt = "/@moosemorals\\.com$/";
  priority = low;
  apply { actions { reject = 1000.0; greylist = 1000.0; "add header" = 4.5; } }
}
```

The answer from `/internal/mail/settings` may carry `symbols`, score thresholds and the account's
`want_spam`, and **never `actions`**: a settings response can lower `reject`, and `reject = 1000` is the
invariant that keeps the filter advisory. `mail-check` asserts the shape.

Several recipients in one message is the corner the plan names: Rspamd applies one settings block per
message, so the first recipient in the array decides, and a message addressed to two accounts sits in
whichever one wins. It is rare, it costs a score rather than a message, and the static floor still
applies.

If the fallback is ever wanted, it renders one block per account with `rcpt` = the mailbox, its aliases
and its plus pattern (`"/^osric\\+.*@moosemorals\\.com$/"`), because plus addresses are never aliases.

## The website's routes

Three service routes in the shell (Decision 7). Authentication is split by what a request carries:
**HTTP Basic with the credentials being verified** on the auth route, and **one `Bearer` service
credential** on the two that must answer without a password. Never both on one request.

Dovecot's userdb is a `static` block in its config, not a call: delivery must keep working while the
site is down, and LMTP has no passdb to carry a credential. That is why there is no userdb function
below — and why `auth_password_verify` returning a fields table would not have saved a round trip
anyway, since Dovecot still consults the userdb once one is configured.

The Lua API here is checked against the 2.4 source: `script_init`, `dovecot.http.client`,
`request:add_header`, `response:status()`, `dovecot.base64`, and results from `dovecot.auth`. A wrong
password is `PASSDB_RESULT_PASSWORD_MISMATCH`; a site that cannot answer is
`PASSDB_RESULT_INTERNAL_FAILURE`, which Dovecot treats as a tempfail — never as a "no".

```lua
-- deploy/mail/dovecot/auth-http.lua (shape)
local http_client

function script_init()
  http_client = dovecot.http.client({
    connect_timeout = "2s", request_timeout = "5s", user_agent = "dovecot/neith",
  })
  return 0
end

-- passdb: the caller *is* the user, so the credential being checked is the one presented.
function auth_password_verify(request, password)
  local req = http_client:request{
    url = "http://169.254.42.42:8080/internal/mail/auth", method = "POST",
  }
  req:add_header("Authorization", "Basic " ..
    dovecot.base64.encode(request.user .. ":" .. password))
  local res = req:submit()

  if res:status() == 200 then
    return dovecot.auth.PASSDB_RESULT_OK, nil
  elseif res:status() == 401 then
    return dovecot.auth.PASSDB_RESULT_PASSWORD_MISMATCH, nil
  end
  -- 5xx, a timeout, a wrong service token: a fault on our side, so tempfail and say why.
  return dovecot.auth.PASSDB_RESULT_INTERNAL_FAILURE,
         "mail auth: site answered " .. res:status()
end

```

The map gateway is the other consumer, and the only one that talks to Postfix:

```text
# the whole protocol, from Postfix's socketmap_table(5): netstring in, netstring out
client → (len:"virtual shopping@moosemorals.com")
server → (len:"OK osric@moosemorals.com")     |  NOTFOUND  |  TEMP site unreachable

# which the gateway answers by asking the site (Decision 8)
POST /internal/mail/lookup {"map":"virtual","key":"shopping@moosemorals.com"}
→ 200 {"value":"osric@moosemorals.com"}   →   OK osric@moosemorals.com

# and the test, which needs no code of ours
postmap -q 'shopping@moosemorals.com' socketmap:unix:/run/maps/socketmap:virtual
```

Every query is one `POST`, and the reply is the HTTP status: `200` → `OK`, `404` → `NOTFOUND`, and
anything else — timeout, `5xx`, refused connection, a `401` from a mistyped token — → `TEMP`, so no
failure of ours can bounce a sender's mail. Postfix closes an idle socketmap connection after ten
seconds, so the gateway is accept-read-request-reply-close and keeps nothing per client.

```json
// POST /internal/mail/auth — HTTP Basic: base64(username:password), no body, no service token
//   200   these credentials are right      → Dovecot logs the user in
//   401   they are not (unknown or wrong)  → Dovecot says no, identically for both
//   5xx   the site cannot answer right now → Dovecot tempfails, never a false "no"

// POST /internal/mail/lookup → 200; 404 is a real "no such address", anything else is deferred
{ "map": "virtual", "key": "shopping@moosemorals.com" }
{ "value": "osric@moosemorals.com" }

// the same route, the other map: the mailbox path Postfix expects
{ "map": "vmailbox", "key": "osric@moosemorals.com" }
{ "value": "/var/mail/osric@moosemorals.com" }

// POST /internal/mail/settings → the recipient's actions; 404 means "use the default"
{ "actions": { "reject": 1000.0, "greylist": 1000.0, "add header": 4.5 } }
```

## Caddyfile

```caddyfile
# deploy/Caddyfile — the mail host has no website. It exists for MX, IMAP and its certificate.
neith.moosemorals.com {
	respond "there is no web content on the mail host" 410
	header -Server
	header -Via
}

# ... and the website's block refuses the mail routes, which live in the same app but are
# reachable only on the app's loopback listener (Decision 7).
moosemorals.com {
	# ... the existing site ...
	handle {
		@internal path /internal/*
		respond @internal 404
		@metrics path /metrics
		respond @metrics 404
		reverse_proxy 127.0.0.1:8080
	}
}
```

Caddy's ACME HTTP-01 handler runs ahead of the mail host's catch-all, so a bogus challenge path gets
Caddy's `404` rather than the `410`.

## DNS

```json
{
  "zone": "moosemorals.com",
  "ttl": 3600,
  "records": [
    { "host": "@",    "type": "MX",   "priority": 10, "data": "neith.moosemorals.com." },
    { "host": "@",    "type": "TXT",  "data": "v=spf1 ip4:server:ipv4 ip6:server:ipv6 -all" },
    { "host": "neith", "type": "A",   "data": "server:ipv4" },
    { "host": "neith", "type": "AAAA", "data": "server:ipv6" },
    { "host": "neith", "type": "TXT", "data": "v=spf1 ip4:server:ipv4 ip6:server:ipv6 -all" },
    { "host": "mail._domainkey",       "type": "TXT", "data": "keyfile:moosemorals.com.mail.txt" },
    { "host": "mail._domainkey.neith", "type": "TXT", "data": "keyfile:neith.moosemorals.com.mail.txt" },
    { "host": "_dmarc", "type": "TXT", "data": "v=DMARC1; p=none; rua=mailto:dmarc@moosemorals.com" },
    { "host": "_smtp._tls", "type": "TXT", "data": "v=TLSRPTv1; rua=mailto:tlsrpt@moosemorals.com" }
  ],
  "ptr": [
    { "address": "server:ipv4", "name": "neith.moosemorals.com" },
    { "address": "server:ipv6", "name": "neith.moosemorals.com" }
  ]
}
```

`server:ipv4` and `server:ipv6` are read from the Hetzner API with the token that also sets the PTR,
and substituted wherever they appear — including inside the SPF strings, which is what keeps a literal
`ip4:` true when the server moves. `keyfile:` reads the public half beside the private key.

```ini
# deploy/dns/dns-sync.container — run by the timer, not a service
[Container]
Image=registry.osric.uk/dns-sync:latest
Volume=/etc/mail/dns/records:/etc/mail/dns/records:ro
Volume=/etc/mail/dns/keys:/etc/mail/dns/keys:ro
Volume=/etc/mail/dns/mythic-beasts.token:/etc/mail/dns/mythic-beasts.token:ro
Volume=/etc/mail/dns/hetzner.token:/etc/mail/dns/hetzner.token:ro
Exec=/dns-sync --records /etc/mail/dns/records --apply
```

```ini
# deploy/dns/mail-dns.timer
[Timer]
OnCalendar=*-*-* 04:17:00
RandomizedDelaySec=30m
Persistent=true
```

`--apply` in the shipped unit because the diff is printed either way and the log is the audit trail.
`--prune` is a manual run, never a timer.
