<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Email configuration sketches

The container, pod and daemon excerpts for [the email plan](email.md), kept apart so the plan reads as
decisions and phases rather than configuration. Nothing here is deployed: the plan has not started.

The following are the load-bearing parts, not a complete configuration. They are here so the
implementation has a shape to test against.

## Pod

```ini
# deploy/mail/mail.pod
[Pod]
PodName=mail
PublishPort=25:25
PublishPort=587:587
PublishPort=993:993
# Optional: the Rspamd WebUI, reachable only with `ssh -L 11334:127.0.0.1:11334`.
# Set a controller password before publishing it; the UI has no authentication by default.
PublishPort=127.0.0.1:11334:11334
```

```ini
# deploy/mail/postfix.container (excerpt)
[Container]
Pod=mail.pod
ContainerName=mail-postfix
Image=registry.osric.uk/mail-postfix:latest
Volume=/etc/mail/postfix:/etc/postfix:ro
Volume=mail-spool:/var/spool/postfix
Volume=mail-postfix-data:/var/lib/postfix
Volume=/etc/mail/tls:/etc/mail/tls:ro
ReadOnly=true
Tmpfs=/tmp
Tmpfs=/run
NoNewPrivileges=true
PodmanArgs=--memory=512m --pids-limit=256
Exec=postfix start-fg
```

Dovecot gets `mail-data:/var/mail`, `mail-dovecot-state:/var/lib/dovecot`, its config and Sieve
directories, and the same read-only `/etc/mail/tls` (the synced copy, not Caddy's store). Rspamd
gets its config, `mail-rspamd:/var/lib/rspamd`, and the DKIM key. The pod
starts the three containers together; `[Install] WantedBy=multi-user.target` goes on the pod and on
each container service, as with the existing rootful Caddy unit.

The entrypoint must seed state on an empty volume: `postfix check` creates the queue structure,
and Dovecot needs `/var/mail` owned by the `vmail` user (uid/gid 5000). Both are idempotent and
run on every start.

## Postfix (excerpt)

```ini
myhostname = mail.moosemorals.com
mydomain = moosemorals.com
myorigin = $mydomain
mydestination =
local_recipient_maps =
inet_interfaces = all
inet_protocols = all
compatibility_level = 3.6

virtual_mailbox_domains = moosemorals.com
virtual_mailbox_maps = texthash:/etc/postfix/vmailbox
virtual_alias_maps = texthash:/etc/postfix/virtual
virtual_transport = lmtp:inet:127.0.0.1:24
recipient_delimiter = +

smtpd_banner = $myhostname ESMTP
maillog_file = /dev/stdout
smtpd_helo_required = yes
mynetworks = 127.0.0.0/8
smtpd_relay_restrictions = permit_mynetworks permit_sasl_authenticated reject_unauth_destination
smtpd_recipient_restrictions = permit_mynetworks permit_sasl_authenticated reject_unauth_destination reject_unlisted_recipient

smtpd_sasl_type = dovecot
smtpd_sasl_path = inet:127.0.0.1:12345
smtpd_sasl_security_options = noanonymous

smtpd_tls_cert_file = /etc/mail/tls/mail.moosemorals.com.crt
smtpd_tls_key_file  = /etc/mail/tls/mail.moosemorals.com.key
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

smtpd_sasl_auth_enable = no
```

`master.cf` adds the submission service with `smtpd_sasl_auth_enable=yes`,
`smtpd_tls_security_level=encrypt`, `smtpd_client_restrictions=permit_sasl_authenticated,reject`
and `milter_macro_daemon_name=ORIGINATING`. The `smtp` service keeps SASL off.

## Dovecot (excerpt, 2.4 syntax)

```ini
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

passdb passwd-file {
  passwd_file_path = /etc/dovecot/users
  default_password_scheme = ARGON2ID
}
userdb passwd-file {
  passwd_file_path = /etc/dovecot/users
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
ssl_server_cert_file = /etc/mail/tls/mail.moosemorals.com.crt
ssl_server_key_file  = /etc/mail/tls/mail.moosemorals.com.key
ssl_min_protocol = TLSv1.2
auth_allow_cleartext = no
auth_mechanisms = plain login
auth_failure_delay = 2 secs

mail_plugins = quota sieve
quota = maildir:User quota
sieve_script before {
  type = before
  path = /etc/dovecot/sieve/before.sieve
}
```

No `protocols { pop3 = yes }`, no `dovecot-pop3d` package, no 110/995 listener. The passwd-file
carries `uid:gid:home` per line, so no `default_fields` is needed. `sieve_script before` is the 2.4
replacement for 2.3's `sieve_before`; confirm the exact block against `doveconf` in the image. The
config mount is read-only, so the compiled `.svbin` needs a writable `bin_path` (or a tmpfs at the
Sieve directory); do not let Dovecot try to write beside a read-only `before.sieve`.
Dovecot 2.4 is a genuine configuration break from 2.3: most examples online are 2.3, and
`ssl_cert`/`ssl_key`, `mail_location` and `%u` are gone. Budget time for that, and test with
`doveconf -n`.

## Rspamd (excerpt)

```hcl
# local.d/actions.conf — never reject, never greylist
reject = 1000.0;
greylist = 1000.0;
add_header = 4.0;
```

```hcl
# local.d/dkim_signing.conf
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
# local.d/settings.conf — the deploy concatenates settings.d/*.conf into this file
authenticated {
  priority = high;
  authenticated = yes;
  apply {
    groups_disabled = ["rbl", "spf", "surbl"];
  }
}
```

The `authenticated` block turns off inbound scoring for submission; `dkim_signing` still signs.
The per-account blocks above follow it.
