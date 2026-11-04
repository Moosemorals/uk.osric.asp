<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Semantic HTML and accessibility across the shell

Implementation plan for [BUG-16](../jobs/BUG-16.md) — audit every server-rendered view for the
semantics it claims, replace generic wrappers where an element already means the right thing, and
make the parts of accessibility that can be asserted into tests rather than a habit. The markup is
also where the shell's duplication is worst, so the audit carries the refactor: one document shell,
one diagnostics renderer, one navigation renderer, one antiforgery field, one URL builder.

## Status

Not started. Written on 2026-11-04 against `trunk` at `d3d602b`, where `make test` is green at 218
core, 68 runtime, 432 web and 84 integration tests. [BUG-13, BUG-15, BUG-14 and the editor's line
gutter](../jobs/README.md#closed) have landed, so the editor markup this plan changes is the current
one.

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
served verbatim and get no layout, so this audit is about the shell's own thirteen views.

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
| restore bundled version | `AdminRestoreEndpoint.WriteConfirmationAsync` | `h1`, `p`, `pre.source`, form, `nav` | no `main`; focusable scroll |

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

## Verification

Automated, in `make test`:

- `AccessibilityTests` — the ten shared rules over all thirteen views, plus the per-view assertions.
- `PageLayoutTests` — gutter, nav rendering and encoding, the diagnostics list and its links.
- `PageAssetSeederTests`, `PageAssetTests`, `StatusPageTests` — `/layout.css` is seeded and served.
- `EditorEndpointTests`, `ErrorEndpointTests`, `StatusEndpointTests`, `LoginEndpointTests` — the
  view-specific markup as it now is.
- `SecurityHeaderTests` unchanged: nothing here adds inline script or style.

Manual, once, with `make run` and a signed-in browser:

| Check | How |
| --- | --- |
| Landmarks and reading order | accessibility tree in the browser's devtools, per view |
| Tab order | keyboard only, from the address bar to the last control; the skip link first, focus visible throughout |
| The gutter's role | confirm the numbers are absent from the tree and the diagnostic link announces its position in words |
| Charts | confirm each `svg` is announced by its `<title>`/`<desc>` and the legend is read as a list |
| Zoom and reflow | 200% text-only zoom and a 320px viewport; the editor's `dvh` layout is the risky one |
| Contrast | devtools picker on the gutter, the diagnostics colours, the banner and the status nav's current item |

## Files touched

New:

- `src/uk.osric.asp.Web/Html/ShellDocument.cs`
- `src/uk.osric.asp.Web/Html/AntiforgeryField.cs`
- `src/uk.osric.asp.Web/Http/PageUrls.cs`
- `src/uk.osric.asp.Web/Pages/Seeds/layout.css`
- `test/uk.osric.asp.Web.Tests/MarkupReader.cs`
- `test/uk.osric.asp.Web.Tests/ShellViews.cs`
- `test/uk.osric.asp.Web.Tests/ShellDocumentTests.cs`
- `test/uk.osric.asp.Web.Tests/AccessibilityTests.cs`

Changed:

- `Html/PageLayout.cs` — `Nav`, `NavLink`, the unified `Diagnostics`; `Header`/`Footer` move to
  `ShellDocument`.
- `Editor/EditorEndpoint.cs` — the largest change: eight preambles, seven navs, one label, one list.
- `Authentication/LoginEndpoint.cs`, `Errors/ErrorEndpoint.cs`, `Admin/AdminRestoreEndpoint.cs`,
  `Metrics/StatusEndpoint.cs`, `Pages/PageEndpoint.cs` — the document shell, the nav, the label.
- `test/uk.osric.asp.Web.Tests/{PageLayoutTests,EditorEndpointTests,LoginEndpointTests,ErrorEndpointTests,StatusEndpointTests,PageAssetSeederTests}.cs`
- `test/uk.osric.asp.Web.IntegrationTests/{StatusPageTests,EditorEndpointTests,PageAssetTests}.cs`
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
  [IDEA-001](../jobs/IDEA-001.md) and CI in [OPS-03](../jobs/OPS-03.md).
- Wrapping *page* content in the site layout. Pages own their markup and a layout would be a product
  change, not a semantics fix.
- `<section aria-labelledby>` on each `/status` panel. Sixteen named regions is not navigation.
- Turning the pages listing into a table. It has one data field; it becomes a table when it has real
  columns, and that needs `PageHeadSummary` to grow (D11).
- Rewriting the seeded stylesheets: a rule that must reach an existing installation goes in
  `/layout.css`, or the owner restores the bundled page through `/admin/restore`.
- A design pass over spacing, typography or the palette beyond D8.
