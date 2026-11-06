<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Semantic HTML and accessibility across the shell

Implementation plan for BUG-16, now closed and recorded in [the jobs list](../../jobs/README.md) —
audit every server-rendered view for the semantics it claims, replace generic wrappers where an
element already means the right thing, and make the parts of accessibility that can be asserted into
tests rather than a habit. The markup is also where the shell's duplication is worst, so the audit
carries the refactor: one document shell, one diagnostics renderer, one navigation renderer, one
antiforgery field, one URL builder.

## Status

**Implemented, and archived on 2026-11-06.** Written on 2026-11-04 against `trunk` at `d3d602b`;
steps 1–6 are `7d69d1e` and `33f640e`, step 7's walk is recorded under
[Verification](#verification), and step 8 closed BUG-16 in
[the jobs list](../../jobs/README.md#closed). `make test` is green at 263 core, 68 runtime, 561 web
and 106 integration tests, `make format-check` and `make license-check` are clean, and `make audit`
reports "no vulnerable or deprecated packages".

Four later pieces of work changed the shell this plan draws. Each was decided on its own terms, and
they are listed here so that a reader does not take a sentence below for the current code:

- **DEC-115** (`c8317f0`) made every form's fields a `dl` of `dt`/`dd` pairs in a two-column grid,
  with a shared rule to keep it that way, so a label is no longer a bare `<label for>` in a form.
- **DEC-116** (`695f4c0`) links `/layout.css` **first**, then `/site.css` and the view's own sheet,
  reversing D7's order and the stale-seed risk that order was chosen to avoid; the gutter's colour
  moved into `editor.css` with it, since `/layout.css` no longer gets the last word.
- **DEC-121** (`7701085`) removed the skip link: `main` is the body's only element, so the link
  targeted the element immediately after it and passed nothing. The `main` of D1 stands; the link
  waits for the chrome it would skip.
- **DEC-120** (`c099c67`) added an opt-in browser suite, so "no browser test" here means "none in
  the shell's own suites". The page assistant's views also joined the shell after BUG-16 closed, and
  `AccessibilityTests` covers them too.

The decisions D1–D14 are kept as they were argued. Those four are the substantial corrections; the
smaller drift — a name that followed the pipeline (DEC-114, so the editor's field is `template` and
not `content`), views that did not exist yet — is visible in the code and not repeated here.

## Goal and constraints

- Every shell view exposes one `main` landmark, one `h1`, headings without a skipped level, every
  `nav` with a distinct accessible name, every form control labelled, and every `th` scoped.
- Do not turn prose into landmarks and do not add a role that an element already carries.
- Visual behaviour stays as it is, with one deliberate exception: the editor gutter's text is
  darkened to reach 4.5:1 on its own background (D8).
- Encoding does not change. Every dynamic value still goes through `WebUtility.HtmlEncode` at the
  attribute or text boundary; the new helpers encode internally, at the same boundary as today.
- No new package, no npm, no browser test. The checks that can be automated are NUnit assertions in
  `Web.Tests` (D9), because the HTML is server-rendered by our own C# and a browser is not needed to
  read it.
- Anything the new markup needs from CSS reaches an *existing* installation without anyone
  remembering to do anything: seeded pages are never overwritten, so new rules ride in a **new seed
  key** (`/layout.css`) rather than an edit to a deployed sheet (D7). Restoring a seeded page from
  source through `/admin/restore` is the fallback, not the plan.
- `make check` is the gate; nothing here may weaken the security headers or the anti-inline-style
  rule the CSP already enforces.

## What the shell renders today

Everything a visitor sees from the site itself is a C# string. Pages written by the owner are
served verbatim and get no layout, so this audit is about the shell's own fourteen views — the ones
that existed on 2026-11-04; the page assistant's views and `/admin/seeds` joined later and answer to
the same rules.

| View | Producer | Today | What is wrong |
| --- | --- | --- | --- |
| listing `/edit` | `EditorEndpoint.WriteListingAsync` | `h1`, two `h2`, `ul`/`li`, one form | no `main`; every row repeats "Edit", "Generated C#", "History", "Delete" with nothing saying which page, and nothing lines the columns up |
| editor `/edit/{path}` | `WriteEditorAsync` | `h1`, `p`, `pre.diagnostics`, `nav.editor-menu`, two forms, `script` | no `main`; the textarea has no label at all; diagnostics are a `pre` |
| generated source | `WriteGeneratedAsync` | `h1`, `p`, `nav`, `pre.source` | no `main`; the scroll container cannot take focus, so it cannot be scrolled by keyboard everywhere |
| history | `WriteHistoryAsync` | `h1`, `p`, `nav`, `table` | no `main`; `th` without `scope` |
| saved revision | `WriteRevisionAsync` | `h1`, `p`, `nav`, `pre.source` | no `main`; focusable scroll |
| delete, scrub, scrub incomplete | `WriteDeleteConfirmationAsync`, `WriteScrubConfirmationAsync`, `ScrubAsync` | `h1`, `p`, form, `nav` | no `main`; the `503` body is one unbroken line |
| sign in | `LoginEndpoint.WriteFormAsync` | `main`, `h1`, `p.error`, form | the failure is neither announced nor associated with the fields |
| error | `ErrorEndpoint.HandleAsync` | `main`, `h1`, `p`, `section`+`h2`, `pre.diagnostics` | hand-builds the diagnostics block that `PageLayout` already renders |
| page compile failure | `PageEndpoint.WriteCompileErrorAsync` | a `pre` in a bare body | no `main`, no `h1`, title "Main - Error" |
| status | `StatusEndpoint.HandleAsync` | `h1`, `nav.status-nav`, `section.panel`+`h2`, `svg`, `ul.chart-legend` | no `main`; the nav is unnamed; the legend is `list-style: none` with no `role="list"` |
| restore bundled version | `RestoreEndpoint.WriteConfirmationAsync` | `h1`, `p`, `pre.source`, form, `nav` | no `main`; focusable scroll |

The duplication that goes with it: the document preamble is opened by `BeginHtmlAsync` in eight
places and closed by `PageLayout.Footer()` in thirteen; the antiforgery hidden input is written out
by hand in seven; nine `<nav>` elements are built by string concatenation; `EditorUrl`/`PageUrl`
exist twice, and the same `Split('/').Select(Uri.EscapeDataString)` expression appears in both
copies.

## Decisions

### D1 — One document shell owns the head, the skip link, `main` and the foot

`Html/ShellDocument.cs` replaces `PageLayout.Header`/`Footer` and `EditorEndpoint.BeginHtmlAsync`:

```csharp
internal static class ShellDocument {
    public static Task BeginAsync(HttpContext context, string title, int statusCode = StatusCodes.Status200OK,
        string? stylesheet = null, string? bodyClass = null, CancellationToken cancellationToken = default);
    public static Task EndAsync(HttpContext context, CancellationToken cancellationToken = default);
}
```

`BeginAsync` sets the content type, the status, `Cache-Control: no-store`, writes the head with
`/site.css`, the view's own sheet and then `/layout.css` (D7 — it must be able to override a stale
copy of a seeded sheet), writes the skip link, and **opens `<main id="main">`**. `EndAsync` closes
it and the document. Body content on every view belongs inside that element, so a view cannot forget
the landmark, and `#main` always exists.

One behaviour changes with it: the error page gains `Cache-Control: no-store` for anonymous
visitors too, because it embeds a per-request trace identifier. No test asserts the old absence, and
caching a page that names a request is worse than the header.

`PageLayout` keeps what is a fragment rather than a document — `Gutter`, `Diagnostics`, `Nav` — and
its name stays accurate.

### D2 — A navigation landmark is data, not concatenated anchors

```csharp
public readonly record struct NavLink(string Href, string Text);
public static string Nav(string label, IReadOnlyList<NavLink> links);
```

The label is a required parameter, so an unnamed `nav` cannot be written. All nine call sites
become a `List<NavLink>` and one call. The existing labels — "Editor navigation", "Confirmation
navigation", "Revision navigation", "Recovery navigation", "Admin navigation" — are kept; `/status`
gains "Time window". `HtmlEncode` moves inside the renderer, so call sites stop pre-encoding whole
URLs and stop re-encoding an already-encoded attribute.

### D3 — The antiforgery field is written once

`Html/AntiforgeryField.cs` — `Hidden(AntiforgeryTokenSet tokens)` — replaces seven hand-copied
`<input type="hidden" …>` strings. SEC-02 put the same token on every mutating form; one renderer
means a change to that contract cannot be applied to six views and forgotten in the seventh.

### D4 — URLs are built in one place and encoded at the attribute

`Http/PageUrls.cs`:

```csharp
public static string Page(PagePath path);                       // /hello%20world
public static string Editor(PagePath path);                     // /edit/hello%20world
public static string EditorCommand(PagePath path, string command);   // …?cmd=history
public static string EditorRevision(PagePath path, string hash);     // …?cmd=revision&hash=…
public static string Restore(PagePath path);                    // /admin/restore?path=…
```

The methods return unescaped URLs for the HTML layer to encode with everything else. That is the
rule the shell is missing today: `WebUtility.HtmlEncode(EditorUrl(path) + "?cmd=generated")` encodes
a URL that was already partly assembled, which is easy to get wrong and impossible to see. Both
private copies of `EditorUrl` disappear, with them the duplicated escaping expression.

### D5 — One diagnostics renderer for both shapes

```csharp
public readonly record struct DiagnosticLine(string Text, PageDiagnosticSeverity? Severity, int? Line);
public static string Diagnostics(IReadOnlyList<DiagnosticLine> lines, bool linkToLines = false);
public static string Diagnostics(IReadOnlyList<PageDiagnostic> diagnostics, bool linkToLines = false);
```

`ErrorEndpoint`'s stack locations are lines with no severity and no line to link, so they render
plain inside the same block. Four call sites — editor, generated source, page compile failure, and
error details — become one renderer, so the line-gutter plan's note that the change "moves both call
sites and the stylesheet together" becomes a single edit.

### D6 — Diagnostics become a list, and keep their look

```html
<ul class="diagnostics" role="list">
  <li class="diagnostic-error"><a href="#L2">line 2, column 5: error CS0103: …</a></li>
  <li>at Handle(IContext ctx) in /hello:line 4</li>
</ul>
```

- The outer class stays `diagnostics` and the severity classes move onto the `<li>`, so the colour
  rules already in a deployed `site.css` still match by inheritance.
- `role="list"` is deliberate: the stylesheet removes the markers, and Safari drops list semantics
  for `list-style: none`. That is the one place a role is not redundant.
- `<ul>` rather than `<ol>`: diagnostics are a set, not a sequence of steps.
- `editor.js` queries `.diagnostics` and `a[href^="#L"]`; both still match. `editor.css`'s
  `.diagnostics a` rule still matches.
- `PageLayoutTests.DiagnosticsIsByteForByteTheBlockTheErrorPagesRender` is replaced by a test that
  both call sites produce the same list, and the editor tests that assert
  `<span class="diagnostic-error">` move to `<li class="diagnostic-error">`.

### D7 — `/layout.css` is a new sheet, and it is a separation not a workaround

Seeded pages are never overwritten, so a rule added to `site.css` today reaches a new installation
only: on an existing one the owner must restore the bundled page through `/admin/restore`. That is
now a working escape hatch, so this is no longer a reason the edit is impossible — it is the reason
it is not automatic.

The choice is therefore made on its merits, and it stands: `site.css` owns **look** (colour, type,
the banner, the panels) and `/layout.css` owns **layout** (the skip link and its target, the
visually hidden utility, list resets, the focusable code block, the focus ring). Those rules are
about document structure in the way the markup is, so they change with it, in the same commit, and
an installation that takes this release gets them on the next boot without a manual step.

`Pages/Seeds/layout.css` follows the shape of the others (ends by setting
`text/css; charset=utf-8` and `Cache-Control: no-cache`). `ShellDocument` links it after the view's
own sheet, so it can override a stale copy of a seeded sheet, and it carries only what the new
markup needs and the old sheets cannot provide:

- `.visually-hidden` — the skip link, the textarea's label, and nothing else.
- `.skip-link` — off-screen until focused.
- `.pages` — the listing rows as an aligned grid (D11), so the action links line up in columns
  without a `table` element.
- `.diagnostics` as a list: no markers, no indent, monospace, `white-space: pre-wrap`; the padding,
  background and border-left keep coming from `site.css`, which still applies.
- `.source:focus-visible` — a visible outline for the focusable code block.
- `.editor .gutter > span { color: #595959 }` — D8.

Rejected: editing `site.css`, `editor.css` or `status.css` for these rules. Not because it is
unrecoverable — `/admin/restore` covers that — but because it puts layout in the look sheet, needs a
manual restore per installation, and leaves the diagnostics list reset and the skip link living in a
file named for the site's appearance. If the split ever proves to be more file than it is worth, the
rules fold back into `site.css` and the seeded page is left in place; nothing in the markup depends
on which sheet carries them.

### D8 — The gutter's grey is darkened

`#767676` on `#f5f5f5` is about 4.1:1, below AA for the 1rem text it is. `#595959` is about 6.4:1 and
is the only colour change in this work. It is not a WCAG failure in the strict sense — the number is
decorative and the diagnostic link says the same position in words — but the numbers are read by
sighted users and there is no reason to leave them marginal.

### D9 — The accessibility checks are assertions, with a hand pass for what assertions cannot see

`test/uk.osric.asp.Web.Tests/MarkupReader.cs` reads start tags, end tags and attributes out of a
rendered string, tracking whether a control is inside a `<label>`. That is enough for the rule set,
and it is honest about why it works: the HTML is written by our own C#, every value is encoded at
the boundary, so `>` and `"` never appear inside an attribute value. A parser dependency, or a
browser, would be a large tool to check a set of views we generated ourselves.

`test/uk.osric.asp.Web.Tests/AccessibilityTests.cs` renders each view once through
`ShellViews.cs` (the endpoint fixtures already have the plumbing: `TestHttp`, `PageHarness`,
`FakePageStore`, `TestAntiforgery`, and a stubbed `IMetricsSource` as `StatusEndpointTests` uses) and
applies one rule set to every document:

1. one `<html lang>`, one `<head>`, one `<body>`, exactly one `<main>`, exactly one `<h1>`;
2. heading levels never skip, and the first is `h1`;
3. every `<nav>` has a non-empty name and no two share one;
4. every `<table>` has a `<thead>` and every `<th>` has `scope`;
5. every form has a submit control;
6. every `input` (except `hidden`), `select` and `textarea` is labelled by `<label for>`, an
   enclosing `<label>`, or `aria-label`/`aria-labelledby`;
7. no `role` that repeats the element's implicit role (`navigation`, `main`, `banner`, `list` on a
   plain `<ul>`, …), except the documented `role="list"` of D6 and `.chart-legend`;
8. no `aria-hidden="true"` subtree containing a focusable element;
9. no `style` attribute and no `<style>` element — the policy the CSP already states;
10. every `<img>` and every `<svg role="img">` has a name (`alt`, `<title>`).

Per-view assertions sit beside the shared set where the rule is about that view: the textarea's
label, each listing link naming its page, the login error's `aria-describedby`, the table's `scope`,
and the two focusable code blocks.

**Checks that stay manual**, because no assertion over a string can make them: the accessibility
tree, tab order and focus visibility, zoom and reflow, colour contrast, and whether a chart's
`<title>`/`<desc>` reads as a sentence. The verification step below is the procedure, and its
findings are recorded here the way the line-gutter plan recorded its three corrections.

Rejected: axe-core or pa11y (npm tooling this repository deliberately does not have, and a browser
to drive); a browser suite (that is IDEA-001's follow-up and OPS-03's CI); a third-party HTML parser
(a dependency to test our own markup).

### D10 — The editor's field gets a real label

`<label class="visually-hidden" for="content">Page source</label>` inside the form, before
`.editor`. Chosen over `aria-label="Page source"` on the textarea: a label element is the element
the audit is asking for, and if `/layout.css` is ever missing the text simply becomes visible, which
degrades better than an attribute nobody can see.

The gutter stays `aria-hidden="true"` and stays out of the accessibility tree; that decision was
made and argued in [the line-gutter plan](editor-line-gutter.md#why-the-gutter-is-scripted) and this
audit agrees with it.

### D11 — The listing stays a list, says which page a link belongs to, and looks aligned

Each per-row link keeps its visible text and gains an accessible name that contains it:
`<a href="…" aria-label="Edit /hello">Edit</a>`. Twenty rows of "Edit" is a list a screen-reader user
cannot navigate; the visible text stays visible, so WCAG 2.5.3 (label in name) holds. A visually
hidden span would need CSS inside the link for no extra clarity.

`<ul class="pages">`, aligned by `/layout.css`, rather than a `table`. The listing looks tabular but
is not: `PageHeadSummary` carries the path and a deleted flag, so the only data field is the path and
everything else in the row is a verb. A table would need column headers reading "Edit / Generated
C# / History / Delete", which name actions instead of describing data, and the rows are ragged —
live rows carry four links, deleted rows three, `: unavailable` rows none — so the missing cells
would exist only to keep the grid square. Grid columns give the same alignment for free, and the
`aria-label` supplies the row context a `<th scope="row">` would have supplied.

If the listing ever grows real columns (compile state, last modified, revision count) it becomes a
table with an `Actions` cell holding the link list, and `PageHeadSummary` grows to feed it. That is
a product change, not part of this audit.

### D12 — The sign-in failure is announced and described

`<p class="error" id="login-error" role="alert">`, with `aria-describedby="login-error"` on the
username and password inputs, and `aria-invalid="true"` on both when a failure is being shown. Both
fields are part of the credential pair that failed, so marking both is honest. Focus is not moved:
the reload announces the alert, and stealing focus on a `POST` response is worse than leaving it.

### D13 — Table headers are scoped; the table gets no caption

`<th scope="col">` on the history table's five columns. No `<caption>`: the `h1` already names the
view, this is the page's only table, and a visually hidden caption needs a rule that only applies to
`table-caption` layout — a visual risk for no navigational gain. BUG-16 asks for table headers, and
`scope` is what makes the header association explicit rather than inferred from position.

### D14 — A scrollable code block can take focus

`<pre class="source" tabindex="0">` on the generated, revision and restore-preview blocks. A region
that scrolls but cannot be focused cannot be scrolled from the keyboard in every browser. No
`role="region"` and no label: a landmark per code block would be noise, and the `<pre>`'s text is
already read.

## Implementation sequence

Test-first throughout: each step starts with the assertion that fails.

**Step 1 — the refactor, with no markup change.** `ShellDocument`, `NavLink`/`Nav`,
`AntiforgeryField`, `PageUrls`, the unified `Diagnostics`. Convert all call sites. Every existing
markup assertion must still pass unchanged — that is the whole point of doing this step separately
from the semantics. Expect the two `Header` tests to move to `ShellDocumentTests`, and nothing else.

**Step 2 — `/layout.css`.** Add the seed, link it last in `ShellDocument.BeginAsync`, update
`PageAssetSeederTests` (its `SeedPaths` array is a closed list) and `StatusPageTests` (it asserts the
`status.css` link; it must now also assert `layout.css` follows it). Integration: `/layout.css` is
served, `text/css; charset=utf-8`, `no-cache`, ETag, and is seeded once for an existing database.

**Step 3 — `main`, headings and the skip link.** The document shell opens `main`;
`WriteCompileErrorAsync` grows an `h1` ("Compilation failed") and a real title.
`AccessibilityTests` rules 1 and 2 begin to pass view by view.

**Step 4 — the diagnostics list.** Rules 7 and 10 stay green; the editor's script contract is
re-checked by hand (`.diagnostics`, `#L{n}`, `#content`, `#gutter`).

**Step 5 — view-specific semantics.** Nav labels (`status-nav`), the textarea label, listing link
names, login error wiring, `th scope`, the two `tabindex="0"` blocks.

**Step 6 — the rules over every view.** `MarkupReader`, `ShellViews`, `AccessibilityTests`. Anything
the rule set cannot express is either moved into a per-view assertion or listed as manual.

**Step 7 — verify.** `make check`, then [the manual pass](#verification) below; record what it finds
in this plan, correct the code, and re-run.

**Step 8 — close the job.** Delete `docs/jobs/BUG-16.md`, move the row to the Closed table in
`docs/jobs/README.md` with a one-paragraph fix note, drop it from the rough order, add
`docs/decisions.md` rows for D1, D6, D7 and D9, and update `docs/architecture.md` and
`docs/testing.md` where they describe the layout and the suites.

### As built

Steps 1–6 are `7d69d1e` and `33f640e`. Two details differ from the sequence above, both small:

- Rule 7's sanction became a named predicate — a `ul` whose markers a sheet strips, on
  `.diagnostics`, `.chart-legend` or `.pages` — because the listing's own `role="list"` tripped the
  rule as stated. The listing stays a list, not a table (D11).
- `PageLayout.Diagnostics` takes `IReadOnlyList<DiagnosticLine>` and nothing else. An overload for
  `IReadOnlyList<PageDiagnostic>` made `PageLayout.Diagnostics([])` ambiguous at the call site, so
  the conversion is explicit: `DiagnosticLine.From(diagnostics)`.

## Verification

Automated, in `make test`:

- `AccessibilityTests` — the eleven shared rules over all nineteen views the shell renders, plus the
  per-view assertions.
- `PageLayoutTests` — gutter, nav rendering and encoding, the diagnostics list and its links.
- `PageAssetSeederTests`, `PageAssetTests`, `StatusPageTests` — `/layout.css` is seeded and served.
- `EditorEndpointTests`, `ErrorEndpointTests`, `StatusEndpointTests`, `LoginEndpointTests` — the
  view-specific markup as it now is.
- `SecurityHeaderTests` unchanged: nothing here adds inline script or style.

`make format-check` is clean, `make audit` reports "no vulnerable or deprecated packages", and
`make test` is green at Core 263, Runtime 68, Web 561, Integration 106 — checked on 2026-11-06, so
these are today's counts rather than the 218/68/455/84 the work closed on.

### The walk

`make run` against a throwaway database (`ConnectionStrings__Storage=/tmp/a11y/storage.db`) with one
account, driven by a scripted session: `/login`, a failed `/login`, `/edit`, `/edit/hello` clean and
with diagnostics, `?cmd=history`, `?cmd=revision`, `?cmd=generated`, `?cmd=delete`, `/status`, and a
template that does not compile. Each answered with the shell — the skip link first, one
`main id="main"`, one `h1`, `/site.css`, the view's own sheet where it has one, `/layout.css`.

| Check | What the walk showed |
| --- | --- |
| Landmarks and reading order | `main#main` is the body's only element after the skip link on every route; one `h1` each ("Pages", "Editing /hello", "History for /hello", "Saved revision for /hello", "Generated C# for /hello", "Delete /hello?", "Status", "Sign in", "Compilation failed"); the listing's `h2`s have no gap, and no view skips a level |
| Tab order | no positive `tabindex` in `src`; the only `aria-hidden` is the gutter, whose children are non-focusable `span`s; the skip link is the first focusable thing in every document; nothing sets `outline: none`, so the only focus styling is additive (`layout.css`) |
| The gutter's role | `aria-hidden="true"` on `div.gutter`, so the numbers are not announced; the diagnostic item reads "line 2, column 12: error CS0029: Cannot implicitly convert type 'string' to 'int'" — the position in words — and its link is the gutter number it names |
| Charts | thirteen `svg.chart` on `/status`, each `role="img"` with a `<title>` naming its series and a `<desc>`; every one is followed by `ul.chart-legend[role="list"]`, so the legend is a list |
| Zoom and reflow | from the sheets, not a render: `100dvh` is on the editor body only, and the risk it carries is contained — `main` is now the body's flex child, `.editor` keeps `min-height: 12rem`, the gutter is `flex: none` and the textarea is `min-width: 0` with `overflow-x: auto`, so a narrow viewport shortens the textarea rather than widening the document; the listing wraps and wraps paths anywhere |
| Contrast | computed from the seed colours, all text past AA: gutter 6.42:1 (`#595959` on `#f5f5f5`, and 4.17:1 before, which is why D8 changed it), diagnostics error 5.40:1, warning 5.44:1, skip link 6.63:1, target line 16.28:1, chart labels 5.74:1 and axis title 9.74:1 |

`editor.js` was the one contract no test can see. It still finds `#content`, `#gutter`, `.editor`,
`.diagnostics`, `a[href^="#L"]` and the `#L{n}` ids, and its diagnostics handler matches on the class,
so the block changing from `pre` to `ul` does not reach it.

### Found, and deliberately not fixed here

- The chart series colours fall below the 3:1 non-text threshold on white: `#ff7f0e` 2.53:1,
  `#2ca02c` 3.40:1, `#9467bd` 4.26:1. The `<title>`, the `<desc>` and the legend name the series, but
  telling two lines apart on the chart itself is colour only. A palette that clears 3:1 is a design
  pass, not a shell change.
- The history table — five columns, one a 64-character hash — is wider than a 320px viewport and
  scrolls the document sideways. WCAG 1.4.10 exempts parts that need two dimensions, which a data
  table does; a scrolling wrapper would be kinder, but the wrapper is markup in `EditorEndpoint`, so
  it is a follow-up.
- `/status`'s charts are `viewBox`-scaled, so their axis labels shrink with the column at 320px. The
  title, description and legend carry the meaning there; the labels are decoration below that width.
- An unknown path still answers with plain text (`/nope not found.`), so the one response that is not
  a document is the 404. It was like that before this change and it is only one line, but it is the
  place where "every page gets the shell" stops being true.

### Left for a person with a browser

The announcements themselves (VoiceOver or NVDA reading order, the `role="alert"` on a failed sign
in, the gutter numbers being skipped), the visible focus ring, and 200% text-only zoom with a real
320px render — the editor's `100dvh` and the status charts in particular. The CSS above says both
should hold; this was the list of things nobody had seen.

#### Browser pass, 2026-11-06

The parts of that list a browser can answer without a screen reader were walked on a throwaway
database, driven in the VS Code integrated browser (Chromium) at `127.0.0.1:5199`. What it showed:

- **The accessibility tree.** Sign in and its failure, the listing, the editor clean and with
  diagnostics, the generated source, the history table, the compile failure: one `main`, one `h1`,
  `nav` names "Admin navigation" and "Editor navigation", the editor's field announced as
  "Page template", the five history columns as column headers, the diagnostics as a list whose item
  is `line 1, column 12: error CS0029: …` and whose link points at `#L1`. The failed sign-in is an
  `alert`, both fields carry `aria-invalid="true"` and describe it, and focus stays on the document.
- **The gutter.** Absent from an aria snapshot of `main` while the spans are in the DOM with
  `aria-hidden="true"`, which is the decision holding. (The tool's page snapshot does list them; it
  does not apply `aria-hidden`, so the tree has to be read from the element, not the page summary.)
- **Focus.** Nothing sets `outline: none`: links take the browser's own ring, `outline: auto`, from
  the keyboard, and `.source:focus-visible` gives the documented `solid 2px rgb(0, 95, 163)` when
  the generated source is reached by `Tab` — a block that is 991px wide inside a 96px box and so
  genuinely needs the focus that makes it scrollable.
- **Contrast, measured in the browser rather than computed from the sheet:** the gutter's `#595959`
  on `#f5f5f5` is 6.42:1.
- **Reflow.** At 213 CSS px — the narrowest the embedded browser would go, so narrower than the
  320px requirement — `/edit` and `/edit/site.css` keep the document from scrolling sideways: the
  listing wraps to three-line rows and the editor's textarea shrinks to 106px and scrolls its own
  content. The history table does not, at 1110px, and that is the known follow-up above.
- **The skip link.** None, as DEC-121 decided, and no positive `tabindex` anywhere.
- **`/status` was not walked.** Its charts need Prometheus at `localhost:9090`, which this run did
  not have, so the chart half of the earlier table still rests on the markup and the sheets.

Still unseen, and still needing a person: what a screen reader actually announces, 200% text-only
zoom, and a chart's `<title>`/`<desc>` read aloud.

## Files touched

New:

- `src/uk.osric.asp.Web/Html/ShellDocument.cs`
- `src/uk.osric.asp.Web/Html/NavLink.cs`
- `src/uk.osric.asp.Web/Html/DiagnosticLine.cs`
- `src/uk.osric.asp.Web/Html/AntiforgeryField.cs`
- `src/uk.osric.asp.Web/Http/PageUrls.cs`
- `src/uk.osric.asp.Web/Pages/Seeds/layout.css`
- `test/uk.osric.asp.Web.Tests/MarkupReader.cs`
- `test/uk.osric.asp.Web.Tests/MarkupElement.cs`
- `test/uk.osric.asp.Web.Tests/ShellView.cs`
- `test/uk.osric.asp.Web.Tests/ShellViews.cs`
- `test/uk.osric.asp.Web.Tests/ShellDocumentTests.cs`
- `test/uk.osric.asp.Web.Tests/AccessibilityTests.cs`

Changed:

- `Html/PageLayout.cs` — `Nav`, `NavLink`, the unified `Diagnostics`; `Header`/`Footer` move to
  `ShellDocument`.
- `Editor/EditorEndpoint.cs` — the largest change: eight preambles, seven navs, one label, one list.
- `Authentication/LoginEndpoint.cs`, `Errors/ErrorEndpoint.cs`, `Admin/RestoreEndpoint.cs`,
  `Metrics/StatusEndpoint.cs`, `Pages/PageEndpoint.cs` — the document shell, the nav, the label.
- `test/uk.osric.asp.Web.Tests/{PageLayoutTests,EditorEndpointTests,StatusEndpointTests,PageAssetSeederTests}.cs`
- `test/uk.osric.asp.Web.IntegrationTests/{StatusPageTests,PageAssetTests}.cs`
- `docs/architecture.md`, `docs/testing.md`, `docs/decisions.md`, `docs/jobs/README.md`, and the
  deletion of `docs/jobs/BUG-16.md`.

Not touched: `PageAssetSeeder` picks the new seed up from its existing directory scan,
`site.css`/`editor.css`/`status.css` keep their bytes, and no `.csproj` or package file changes.

## Acceptance criteria

- Every shell view has exactly one `main` and one `h1`, no skipped heading level, every `nav` named
  distinctly, every control labelled, every `th` scoped, no redundant role, no inline style.
- The diagnostics block is a list in all four call sites and the editor's diagnostics are still
  links to gutter lines.
- `/layout.css` is a seeded page, linked last on every shell view, and its rules reach an
  installation that ran the previous release.
- No markup is written twice: one document shell, one nav renderer, one antiforgery field, one URL
  builder, one diagnostics renderer.
- The listing is a list whose action links line up, and each names the page it acts on.
- `make check` passes, and the manual table above has been walked and its findings recorded here.
- `docs/` describes the shell as it now is, and BUG-16 is closed.

## Risks

- **Byte-for-byte assertions.** `EditorEndpointTests`, `PageLayoutTests`, `ErrorEndpointTests`,
  `StatusEndpointTests` and the two integration fixtures pin markup. Step 1 is designed to prove the
  refactor changed none of it; steps 3–5 are where they move.
- **`editor.js`.** It depends on `#content`, `#gutter`, `.editor` and `.diagnostics`, and on the
  gutter's `#L{n}` ids. All four survive, but the diagnostics click handler must be re-checked in a
  browser because no test can see it.
- **The reader's assumptions.** `MarkupReader` is simpler than HTML. Keep it in tests, keep a few
  direct string assertions on the markup that matters most, and let it fail loudly rather than
  returning nothing when it sees something it does not expect.
- **The seed order.** `layout.css` must be linked after the view's sheet, or a stale `editor.css` on
  an existing installation wins and the gutter colour change never lands.
- **The label dependency.** If `/layout.css` is missing, `.visually-hidden` does not exist, so the
  skip link and the textarea's label become visible text. That is the intended degradation, but it
  is worth knowing before blaming the markup.

## Not in this phase

- axe-core, pa11y, or any browser-driven accessibility run. It belongs with the Playwright suite in
  [IDEA-001](../../jobs/IDEA-001.md) and CI in [OPS-03](../../jobs/OPS-03.md).
- Wrapping *page* content in the site layout. Pages own their markup and a layout would be a product
  change, not a semantics fix.
- `<section aria-labelledby>` on each `/status` panel. Sixteen named regions is not navigation.
- Turning the pages listing into a table. It has one data field; it becomes a table when it has real
  columns, and that needs `PageHeadSummary` to grow (D11).
- Rewriting the seeded stylesheets: a rule that must reach an existing installation goes in
  `/layout.css`, or the owner restores the bundled page through `/admin/restore`.
- A design pass over spacing, typography or the palette beyond D8.
