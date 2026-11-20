<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The template language server

A Language Server Protocol server over stdio, so an editor underlines a page template's problems while
it is typed. It is not a second checker: `TemplateSourceGenerator`, `TemplateShapeCheck` and
`IPageCompiler` are the product's own, called the way a save calls them, and a diagnostic therefore
arrives in template space — the line and column the author typed.

```bash
make build
dotnet artifacts/bin/asp-language-server/debug/asp-language-server.dll \
    --seeds src/uk.osric.asp.Web/Pages/Seeds
```

The server reads and writes JSON-RPC with `Content-Length` framing. The `--seeds` folder stands in for
the page store, which an editor does not have: every library in it is a candidate for a page's `using`
directives, and a diagnostic inside one is published against that file rather than against the page
that reached it.

## What it speaks

`initialize`, `initialized`, `textDocument/didOpen`, `textDocument/didChange` (full sync),
`textDocument/didClose`, `textDocument/shutdown`, `exit`, and `textDocument/publishDiagnostics` the
other way. Anything else is refused with `-32601`. There is no completion, hover or formatting: those
need the template-to-generated reverse map, and the plan leaves them for a later slice.

The server is one loop, so a document's diagnostics are published for the version that produced them
and a slower check of older text can never replace a newer answer. A closed document's diagnostics are
cleared.

## The suite

`test/asp-language-server.Tests` proves the protocol rather than Roslyn: the framing round-trips a
non-ASCII message, a diagnostic's 1-based template position becomes the protocol's 0-based range, a
change is published for its own version and never an older one, a library a seed names is found in the
folder and its error names the library's file. `make test-language-server` runs it; `make test` and
`make check` include it.
