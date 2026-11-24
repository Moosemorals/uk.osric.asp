<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A page cannot take a file

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

A page cannot take a file: `PageRequest` carries query, form, headers and cookies as text, which is
half of why the archive stays in the shell ([keel](../spikes/keel.md)). Add `Files` — a read-only list
of parts, each with the client's file name, the claimed content type, the host's measured size and a
forward-only stream over the bytes — with the part's stream handed straight to the object store
rather than spooled to a temp root: a streaming put writes it to the BLOB incrementally, and the
render's stream clocks bound the upload instead of a spooling pass before it. The limit is a policy
value, 1 GB per request shipped and counted on the bytes actually written rather than a header's
claim, a part that outruns it is a `413` naming the limit, and the name and type are claims the page
treats as data rather than authority.
