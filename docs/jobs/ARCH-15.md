<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# ARCH-15 — `Assistant:ProtectedPaths` is a second kind of read-only page

| | |
| --- | --- |
| Severity | Low |
| Area | Assistant, configuration |
| Location | `src/uk.osric.asp.Web/Assistant/AssistantOptions.cs`, `src/uk.osric.asp.Web/Assistant/PagePathPolicy.cs`, `src/uk.osric.asp.Web/appsettings.json` |

## Problem

The site has two ways to say "this key is not a page to write": `Pages:ReservedPaths`, which is the
shell's own routes and which the editor, the exporter and the assistant's tools all honour, and
`Assistant:ProtectedPaths`, which only the assistant honours and which no other part of the site can
see. It was added when the tools had no idea what the shell was, so an operator could keep the
assistant off a sheet or a script; since DEC-180 the shell's routes are that surface and the shipped
list is empty, leaving an option with no UI, no page in the docs of its own and a lock that exists in
one subsystem's configuration. A page can be read-only to the assistant and editable to the owner,
which is a distinction nothing else in the site can express or explain.

## Fix outline

1. Decide whether a *page* can be read-only at all — a property on the head, set from the editor and
   honoured by the editor, the exporter and the assistant — or whether read-only is a property of the
   shell's own keys and nothing else. The first is a storage change and an editor control; the second
   is a deletion.
2. If the answer is deletion: remove `AssistantOptions.ProtectedPaths`, the `IsProtected` branch and
   its "locked read-only" message in `PagePathPolicy`, the tests that pin them, the empty row in
   `appsettings.json` and the reference in [the assistant docs](../assistant.md), so the assistant may
   write every page the shell does not serve a route for.
3. If the answer is a page-level lock: keep `PagePathPolicy` reading it, move the flag into the store,
   give the editor a way to set it, and decide whether import and export carry it — DEC-139 says an
   archive carries source and state only.
4. Either way, DEC-180 and the assistant docs are amended in the same change, because they name the
   option as the operator's lock.
5. Tests: no policy test names a removed option; a page-level lock, if it lands, is proved through the
   editor and the assistant rather than through configuration.
