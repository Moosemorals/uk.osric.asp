<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# DOC-05 — `.editorconfig` still has a Razor section

| | |
| --- | --- |
| Severity | Low |
| Area | Documentation, configuration |
| Location | `.editorconfig:57-66` |

## Problem

The editorconfig contains:

```
# Razor files
...
[*.cshtml]
csharp_new_line_before_open_brace = none
...
```

There are no `*.cshtml` files in the repository, and the architecture is endpoint handlers, not
Razor. Like DOC-01, this signals a project shape that no longer exists and invites new Razor code
that the rest of the repository does not support.

## Fix outline

1. Delete the `# Razor files` section and the `[*.cshtml]` block.
2. Check the remaining `.editorconfig` for other stale rules (e.g. anything referencing MVC or Razor
   naming).
3. If a `*.md` section is ever added for documentation, keep it separate from C# rules.
