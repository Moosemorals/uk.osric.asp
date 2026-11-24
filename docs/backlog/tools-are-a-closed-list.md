<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The assistant cannot reach a tool the host does not hold

| | |
| --- | --- |
| Difficulty | — |
| Utility | — |
| Status | unshaped |
| Blocked by | — |
| Area | — |
| Location | — |
| Spike | — |

## Idea

The assistant cannot reach a tool it does not hold: `ToolRegistry` is a closed list of compiled-in
types, so a capability that belongs in another process — a mailbox over IMAP, a calendar, someone
else's API — can only become more host code. Make the assistant an **MCP client**: configured servers
whose tools are discovered at run time and sit beside the static ones in the registry, each call
approval-gated and each result untrusted data like a fetched body, with the server list a policy
value. The first server is the mailbox — [the email plan](../plans/email.md)'s Dovecot over IMAP, in
its own process rather than in the host.
