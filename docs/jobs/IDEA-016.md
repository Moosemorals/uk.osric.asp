<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-016 — A page cannot save its own source

| | |
| --- | --- |
| Size | Medium |
| Area | Sdk, Core, Web |
| Location | `src/uk.osric.asp.Sdk/IContext.cs`, `src/uk.osric.asp.Web/Editor/PageEditor.cs` |

## Idea

DEC-01 says the product is a page that can edit itself. Today only the shell can save, so "itself"
means the shell edits it. A page already knows its own key (`ctx.Path`), and a write bound to that
key is the same isolation `IPageStorage` uses — so a page can be given the save path without being
given the store.

Two things stand in the way. There is no write member on `IContext`, and there is no diagnostics type
in `Sdk`: `PageDiagnostic` lives in `Core.Compilation`, which a page can neither reference nor load
(DEC-88), and the save path returns it.

The goal is `ctx.SaveAsync(template)`: bound to `ctx.Path`, compiled before it is stored (DEC-19),
answering with the diagnostics and the new revision hash as plain Sdk values.

## Sketch

1. One diagnostic value, in `Sdk`: severity, 1-based template line and column, author-facing code and
   message — the fields `PageDiagnostic` already carries and the editor already shows. `Core` keeps
   its own type and `SimpleContext` maps between them, because moving `Core`'s type into `Sdk` would
   make the generator depend on the page contract for no gain.
2. No path argument. The handle is the isolation, exactly as `IPageStorage` is bound to
   `IContext.Path`; a page that needs to write elsewhere is IDEA-017 and a different decision. A
   `HEAD` render refuses the write, as storage does.
3. Authority stays with the shell. Refuse the save unless the visitor is signed in, and refuse a
   reserved key, in `SimpleContext` and not in page code: an anonymous page with a save form is a
   defacement vector, and the page being defaced is the one that submitted it.
4. Say what a save during a render means. The running image is already loaded, so the write belongs
   to the next request; the response in hand is still the old page unless the page re-reads what it
   just wrote. That is the same behaviour as saving from the editor in another tab, and it should be
   documented rather than smoothed over.
5. Reuse `PageEditor.SaveAsync` rather than opening a second writer. It already compiles first,
   stores the template with its image, records `page.save.duration` and keeps the source when the
   compiler itself faults (DEC-20, DEC-23). A page's save is a revision like any other.
6. The value returned is what the page needs to render its own answer: a save that compiled clean, a
   save with warnings, or a list of errors with the template positions the author can click. There
   is no streaming and no second render (ARCH-13): the page saves, then writes its body.
7. Tests: a save round-trips as a revision and the next request serves it; a compile error is stored
   and reported (DEC-23); a save on `HEAD` is refused; an anonymous save is refused; a reserved key
   cannot be reached because there is no key to name.
