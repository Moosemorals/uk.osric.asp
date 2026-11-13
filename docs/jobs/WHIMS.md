<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Whims

Ideas too small or too vague to be a job. One or two lines each, with no size, area or plan. A whim
that grows a shape is promoted to an `IDEA-` file and its line is removed; deleting one is free.

| ID | Whim |
| --- | --- |
| WHIM-01 | Language Server Protocol support for the editor: publish the C# compile diagnostics and the script checker's JavaScript diagnostics to [IDEA-001](IDEA-001.md)'s editor over LSP. |
| WHIM-02 | Asp diagnostics — warnings about the template's shape rather than its C#, which the compiler cannot see because none of them is an error: `%><%` with nothing between it, a block that holds only whitespace or a comment, and a `%>` inside a string, which ends the block early and reports `CS1010 Newline in constant` against the quote while the rest of the page quietly becomes text. The tokenizer already has every one of these, so they need no Roslyn; they belong beside the compile diagnostics [IDEA-029](IDEA-029.md) would publish. |
