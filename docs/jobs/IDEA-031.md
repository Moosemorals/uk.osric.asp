<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-031 — Some policy is still a constant in code

| | |
| --- | --- |
| Size | S |
| Area | Core, Web, Runtime (deploy) |
| Location | `src/uk.osric.asp.Web/Users/PasswordPolicy.cs`, `src/uk.osric.asp.Web/Users/UserName.cs`, `src/uk.osric.asp.Web/Http/SecurityHeaders.cs`, `src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs`, `src/uk.osric.asp.Web/Pages/PageEndpoint.cs`, `src/uk.osric.asp.Web/Pages/PageRenderer.cs`, `src/uk.osric.asp.Web/Storage/` |

## Idea

[Configuration](../architecture.md#configuration) is now the only source of a policy value: a missing
or unusable one stops the site at startup. What is left in code is deliberate, and listed here so the
exception does not become silent.

| Value | Where | Why it stays |
| --- | --- | --- |
| Password length 12–1024 | `PasswordPolicy` | DEC-48 fixes it. A shorter password is a weaker one, so the only direction configuration could move it is down |
| User name length 64 | `UserName` | The same number is a column length and four EF models' `HasMaxLength`; it is a schema constraint before it is a policy |
| The header set and the CSP | `SecurityHeaders` | DEC-34 and the frame, referrer and permissions rules. A wider policy is the only thing a setting could ask for |
| The page API allow-list | `PageApiPolicy` | DEC-35 makes it default-deny data in `Core`; a page that could change it would be granting itself APIs |
| The 1 MiB form body, and the parser's count and header limits | `PageEndpoint`, `Program` | DEC-34. The upload cap that is policy is already `Pages:Archive:MaxUploadBytes` |
| Compile attempts, SQLite retries | `PageRenderer`, `SqliteErrors` | Internal recovery behaviour: a different number is a different algorithm, not an operator preference |
| Cleanup and sample periods | the background services | Implementation detail with no observable policy behind it |

## Sketch

Two questions, and each value answers one:

1. **Can configuration only tighten it?** If yes, expose it as a key whose accepted range starts at
   the shipped value, so an operator can be stricter and never looser. The CSP and the password
   maximum are plausible; the password minimum is not, because a shorter password is not a
   legitimate choice for this site.
2. **Is it a constraint or a preference?** A schema length, a column type or a wire-format number is
   a constraint: it changes only with a migration, and configuration cannot express that. Those stay.

If a value is exposed, it gets the same shape as every other policy value — a required key in
`appsettings.json`, a `ConfigurationReader` read with a range that cannot weaken the guarantee, and
the three tests the [testing](../testing.md#configuration) convention asks for. A value that stays
gets a sentence in the table above saying why, so the next reader does not have to rediscover it.

## Not in scope

Moving the numbers wholesale. A setting nobody can safely change is a knob that only documents its
own footgun; this job exists to decide which of these a competent operator would actually turn.
