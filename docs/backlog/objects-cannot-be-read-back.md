<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The assistant cannot read an object it was given

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

The assistant cannot read a page's objects: `IObjectStore` is bound to one subject, offers no listing
and is unreachable from any other subject's handle, so `ls`, `stat` and `cat` cannot answer what a
page's images are and the shell's `/o` mount was dropped from
[the shell plan](../plans/assistant-shell-tool.md). A page capability that opens any page subject's
object by digest — with its content type and length — and lists one page's objects is what the shell
needs, and it belongs beside [`object-access-is-only-its-hash`](object-access-is-only-its-hash.md)'s
decision about who may read one.
