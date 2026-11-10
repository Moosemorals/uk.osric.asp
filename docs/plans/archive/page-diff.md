<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Diff for pages and the assistant

| | |
| --- | --- |
| Status | Implemented 2026-11-10 |
| Size | Medium — a pure line diff, one capability, one renderer, one assistant tool, one editor view, and the tests for each |
| Spike | None. The BCL has no diff, and the algorithm is a page of code rather than a dependency to measure |
| Decisions | DEC-217–DEC-219, recorded in [decisions](../../decisions.md#page-contract-and-threat-model) |
| Backlog | [WHIM-02](../../jobs/WHIMS.md) — the diff viewer; the whim was removed when this landed |

Implementation notes, where the code differs from the plan: the compare form sits **below** the
history table, so the table stays newest first; `DiffRenderer` exposes `Compare` and a
`ToUnifiedDiff(DiffDocument)` overload as well as `ToUnifiedDiff(string, string)`, so the assistant
tool computes the comparison once for its counts and its text; and the page-author prompt moved to
`page-author-v17` with the Diff section.

## The short version

The editor can show a saved revision but not what changed between two of them. This adds a line diff
with three readers, all of them the same implementation:

- **A pure diff in `Core`.** `LineDiff.Compare(before, after)` is a bounded Myers diff over lines,
  with common prefix and suffix trimming, three lines of context per hunk, and a whole-block
  fallback when two documents differ too much to align. `UnifiedDiff.Format` writes the same result
  as unified text. There is no package and no process: .NET's BCL has no diff, and the repository's
  package rule (DEC-15) would need a stronger reason than a page of code.
- **A page capability.** `IDiffRenderer.ToHtml(before, after)` is offered through `ctx.Features`,
  exactly as `IMarkdownRenderer` is. It writes a `table.diff` and escapes every source line, so a
  page may pass visitor or fetched text to it. It is the Markdown renderer's shape with the opposite
  trust rule: Markdown is author markup, a diff is data.
- **An assistant tool.** `diff_page(path, fromHash?, toHash?)` compares two retained revisions,
  defaults to the newest two, and returns a unified diff plus the editor link. It calls
  `DiffRenderer.ToUnifiedDiff`, the renderer's text form, as the Markdown renderer offers the
  assistant a restricted second form. It is read-only and needs no approval. The transcript shows
  the tool result as text; rendering HTML there would make model-adjacent content a second markup
  sink.
- **An editor view.** The history page gains a small GET form with two revision selects. Choosing
  two and pressing Compare opens `?cmd=diff&from=…&to=…`, which loads both retained templates,
  renders them through the same `DiffRenderer`, and writes no state and compiles nothing.

The diff is never stored as its own value. It is derived from two templates on demand, so a revision
cannot disagree with a diff that was saved beside it; the assistant's tool result is an ordinary
transcript message, stored and bounded like every other tool result.

## Why these choices

### Our own line diff

The BCL has no diff algorithm, and the two alternatives are worse than writing one:

- **`diff` as a child process** would add a runtime dependency the container does not necessarily
  carry, put source on a pipe outside the host's own memory limits, and need a parser for output
  that varies by implementation. The host already avoids a process boundary for pages (SEC-03), and
  a diff is not the place to introduce one.
- **A NuGet package** is a dependency for an algorithm that is small, well understood and easy to
  test. DEC-15 asks a package to earn its place; a line diff does not have the native, I/O or
  complexity justification Markdig had.

A line diff is also the right unit for page source. A page is a template of lines; a word or
character diff would add intra-line markup, more tests and a second set of accessibility rules for
a change that is already obvious from the line markers.

The implementation is deliberately bounded. Common prefix and suffix trimming turns the ordinary
"one edit in a large page" case into a tiny problem. Past `MaxEditDistance`, the middle is shown as
one removed block followed by one added block and `DiffDocument.IsCoarse` says so. That is not a
truncated diff — every line is still present — but it is honest about not having aligned the middle.
The cap is what keeps a page that diffs two unrelated maximum-size templates inside its render
budget.

### A capability, not more `IContext`

DEC-142 and DEC-148 settled the seam: something a render may offer is read from `ctx.Features`, and
`IContext` grows only for what every page always has. Diffing is a pure renderer like Markdown, so it
belongs in the same place, is registered for every layer by `PageChain`, and is absent for a page
that never asks for it. A page that wants to compare its own history can do so with `IPageCatalog`
and the capability, without the host growing a diff view it cannot reach.

### The renderer escapes; Markdown does not

`IMarkdownRenderer` passes raw HTML through because the author already writes HTML. A diff is
different: its input is often a page source the author wrote, but a page may also diff visitor input,
a fetched body or a memory. The renderer therefore HTML-escapes every line and writes no attribute
from the input. That makes `ToHtml` safe for any string, and the capability's documentation says so
plainly so a page author does not have to guess which renderer has which rule.

### The assistant gets text and a link, not HTML

The transcript already has one safe renderer for model output and one escaping rule for tool
results. A tool result is displayed in a `<pre>` and stored as text; putting HTML inside it would
either render model-adjacent markup or need a second, special-cased sink with its own escaping. The
assistant's job is to read the change and explain it, and a unified diff is the form for that. The
renderer's `ToUnifiedDiff` is that form, and the result also carries the editor URL, so the
assistant can link the owner to the rendered table the editor shows. One engine, two
representations, each safe in the place it is written.

### The editor compares on a GET

A comparison is a read. It is linkable, refreshable and free of a form token, which matches the
existing `cmd=history` and `cmd=revision` views. The two hashes are the whole request; they are
validated as 64 hex characters and then resolved against the page, so a hash remembered from
another page cannot be spent here. Nothing compiles, nothing writes and no session state changes.

## What exists today

| Piece | Today | Used here for |
| --- | --- | --- |
| `Sdk/IMarkdownRenderer.cs`, `Runtime/MarkdownRenderer.cs` | A page capability and its Runtime implementation | The shape `IDiffRenderer` and `DiffRenderer` follow |
| `Web/Pages/PageEnvironment.cs`, `PageChain.cs` | Every layer is offered `IMarkdownRenderer` | Register `IDiffRenderer` once and offer it the same way |
| `Sdk/IPageCatalog.cs` | `RevisionsAsync` and `ReadRevisionAsync` | A stored page can feed its own diff view |
| `Web/Editor/PageEditor.cs` | `ListRevisionsAsync`, `GetRevisionTemplateByHashAsync` | The editor and the assistant tool load retained templates |
| `Web/Editor/EditorEndpoint.cs` | `cmd=history` lists revisions, `cmd=revision` shows one | `cmd=diff` and the compare form on the history page |
| `Web/Assistant/PageToolExecutor.cs` | One `s_tools` table from which schemas and validation derive | `diff_page` |
| `Web/Pages/PageRevisionSummary.cs`, `PageRevisionTemplate.cs` | Hash, saved time, current mark and template | Select labels, view labels and diff input |
| `Core/Pages/RevisionHash.cs` | The 64-character revision hash | The editor's and the tool's handles |

## Design

### D1 — The diff: values in `Core`, one algorithm

`Core` gains a `Diff` folder. The values are immutable and carry no host types, so both the renderer
and the tool can use them:

```csharp
namespace uk.osric.asp.Core.Diff;

public enum DiffLineKind { Context, Added, Removed }

public readonly record struct DiffLine(
    DiffLineKind Kind,
    string Text,
    int? BeforeLine,
    int? AfterLine);

public readonly record struct DiffHunk(
    int BeforeStart,
    int BeforeCount,
    int AfterStart,
    int AfterCount,
    IReadOnlyList<DiffLine> Lines);

public sealed record DiffDocument(
    IReadOnlyList<DiffHunk> Hunks,
    int AddedLines,
    int RemovedLines,
    bool IsCoarse) {
    public bool IsEmpty => Hunks.Count == 0;
}

public static class LineDiff {
    public const int ContextLines = 3;
    public const int MaxEditDistance = 1_000;
    public const int MaxLines = 100_000;

    public static DiffDocument Compare(string before, string after);
}

public static class UnifiedDiff {
    public static string Format(DiffDocument diff);
}
```

The algorithm, in order:

1. **Split** with `StringReader.ReadLine`, so CRLF, CR and LF are one break and a final newline
   terminates the last line rather than creating an empty one. `""` is zero lines and `"\n"` is one
   empty line. Two templates that differ only in line endings therefore compare equal, which is what
   a page author means.
2. **Trim** the common prefix and suffix, remembering their line counts so numbering survives. Either
   side becoming empty emits a pure removal or addition.
3. **Diff the middle** with Myers' O(ND) greedy algorithm, storing the frontier for each `d` so it can
   backtrack. `MaxEditDistance` caps the work; past it, the middle becomes one removed block and one
   added block and `IsCoarse` is set. `MaxLines` refuses to enter the algorithm at all for a pair
   larger than the cap and takes the same fallback.
4. **Group** the edit script into hunks with up to `ContextLines` unchanged lines on each side.
   Changes separated by no more than twice the context distance share a hunk, as a unified diff does.
5. **Number** every line: a context line has both numbers, a removed line has only the before number,
   an added line only the after number. A hunk's start and count are the ordinary unified-diff
   values, including `0,0` for an empty side.

`UnifiedDiff.Format` writes the canonical text for the assistant tool: `@@ -a,b +c,d @@`, then
lines prefixed with a space, `-` or `+`, each terminated by `\n`. An empty document writes the empty
string. There is no `\ No newline at end of file` marker, because the split makes a final newline a
terminator rather than a line and the marker would describe a difference the tool does not show.

The tests pin the exact unified text, because it is a protocol the model reads, not decoration. The
HTML renderer consumes the same `DiffDocument`, so the two cannot disagree about what changed.

### D2 — The renderer: escaped, semantic markup

`Runtime/DiffRenderer.cs` implements `IDiffRenderer` by walking the `DiffDocument` and writing the
following, with every source line encoded by `WebUtility.HtmlEncode`:

```html
<div class="diff-scroll" tabindex="0">
  <table class="diff">
    <thead>
      <tr><th scope="col">Before</th><th scope="col">After</th><th scope="col">Change</th><th scope="col">Line</th></tr>
    </thead>
    <tbody>
      <tr class="diff-hunk"><td colspan="4">@@ -1,3 +1,4 @@</td></tr>
      <tr class="diff-context"><td>1</td><td>1</td><td><span class="visually-hidden">Unchanged</span> </td><td><code>&lt;h1&gt;…</code></td></tr>
      <tr class="diff-removed"><td>2</td><td></td><td><span class="visually-hidden">Removed</span>-</td><td><code>…</code></td></tr>
      <tr class="diff-added"><td></td><td>2</td><td><span class="visually-hidden">Added</span>+</td><td><code>…</code></td></tr>
    </tbody>
  </table>
</div>
```

The details that matter:

- **The table is a table.** It has a head row and every `th` has `scope="col"`, so the shared
  accessibility rules apply without an exception (DEC-164's table rule, extended to this renderer).
  The `div.diff-scroll` is the keyboard-reachable sideways scroll container the source view already
  uses; `layout.css` gives it `overflow-x: auto` and a focus ring.
- **The marker is not the colour.** Added and removed rows carry `+` and `-` and a
  `visually-hidden` word, so the change reads without colour. The row class is for the sheet, not
  for the meaning.
- **Nothing from the input becomes markup or an attribute.** Only the line text is interpolated, and
  only after encoding. No inline style is written, because the CSP forbids it.
- **Empty and coarse are visible.** A document with no hunks writes
  `<p class="diff-empty">No changes.</p>`. `IsCoarse` writes a
  `<p class="diff-coarse">These documents differ too much to align line by line; the changed middle
  is shown as replacement blocks.</p>` before the table. Both are sentences the editor and a page
  can style, not silent behaviour.
- **No title or labels.** The caller owns the heading and the revision links; the renderer owns only
  the comparison. That keeps one renderer usable by the editor, a page and a test without a label
  parameter that every caller would pass.

`site.css` gains the default look: a monospace table, line-number columns, muted hunk rows,
`#e6ffec` for added and `#ffebe9` for removed with dark text. Contrast is a hand-pass item like the
existing sheets; the markers are what carry the meaning if a sheet is missing.

### D3 — The page capability and the renderer's two forms

`Runtime/DiffRenderer.cs` is the host's one diff renderer, shaped like `MarkdownRenderer`: `ToHtml`
is the page and editor form, and `ToUnifiedDiff` is the assistant's text form. Neither passes source
through as markup: both escape it, so each is safe where it is written.

`Sdk/IDiffRenderer.cs` is the page contract:

```csharp
namespace uk.osric.asp.Sdk;

/// <summary>Renders the line changes between two documents as HTML for a stored page.</summary>
public interface IDiffRenderer {
    /// <summary>Renders a line diff of <paramref name="before"/> and <paramref name="after"/>.</summary>
    public string ToHtml(string before, string after);
}
```

The documentation says, in the style of `IMarkdownRenderer`:

- reach it with `ctx.Features.GetRequired<IDiffRenderer>()` and write the result raw with `<%== %>`;
- it is pure, has no I/O, needs no `await`, and is bounded by the render's body allowance when the
  result is written;
- every source line is escaped, so unlike Markdown it is safe to pass a visitor's text, a fetched
  body or page state;
- the markup is a `table.diff` (or `p.diff-empty` / `p.diff-coarse`) with the classes above, so a
  page can style it;
- both arguments must be non-null; a null argument is an `ArgumentNullException`.

`DiffRenderer` also exposes `string ToUnifiedDiff(string before, string after)`, which is
`UnifiedDiff.Format(LineDiff.Compare(before, after))`. It is not on `IDiffRenderer`, because a page
does not need it; it is the assistant's form, exactly as `MarkdownRenderer.ToRestrictedHtml` is not
on `IMarkdownRenderer`.

Wiring follows Markdown exactly:

- `PageEnvironment` gains `Diff`, after `Markdown`;
- `PageChain.CreateLayer` adds `.Add<IDiffRenderer>(_environment.Diff)` beside the Markdown line;
- `Program` registers `DiffRenderer` as a singleton and passes it to the `PageEnvironment`;
- the test harnesses (`PageHarness`, `ShellViews`, `PageEndpointTests`, `PageLayoutChainTests`) gain
  the same value in their constructions.

Adding the interface to `Sdk` changes the Sdk module id, so every stored image is stale and
recompiles lazily. That is the existing `RuntimeId` rule (DEC-26), not a migration or a special
case.

### D4 — The assistant tool

`DiffRenderer` lives in `Runtime`, which `Web` references, so the assistant is offered the renderer
too: if a later transcript view wants the table, it can call the same `ToHtml`. This change keeps
the tool result text because the transcript's tool card is a `<pre>` and the editor link is the
rendered reader.

`PageToolExecutor` takes the singleton `DiffRenderer` in its constructor, beside its other
dependencies, and gains one read-only entry in its `s_tools` table:

```csharp
new("diff_page",
    "Compare two retained revisions of a page and return a unified diff, the two revision "
    + "hashes and the editor link. With no hashes, compares the current revision with the one "
    + "before it. Read-only: it changes nothing and does not need approval.",
    false,
    [
        new("path", ToolArgumentType.String, true),
        new("fromHash", ToolArgumentType.String, false),
        new("toHash", ToolArgumentType.String, false),
    ]),
```

The handler:

1. Validates the path through `PagePathPolicy.Validate(path, write: false)` and refuses a shell file
   explicitly, because a shell file has no page source and no revisions; `check_script`'s refusal is
   the sentence to copy.
2. Reads `PageEditor.ListRevisionsAsync(path)` newest first.
3. Uses a supplied hash or the default: `to` is the newest revision, `from` is the next one. With
   fewer than two revisions and a missing hash, returns a failure that says so rather than guessing.
4. Loads both templates with `GetRevisionTemplateByHashAsync` and requires each revision's `Path` to
   equal the requested page. A hash from another page is not found, exactly as the editor's revision
   view treats it.
5. Calls `DiffRenderer.ToUnifiedDiff`; caps the text at `MaxToolResultChars / 2` (the same half-budget
   `read_page`, `fetch` and `shell` use) and sets `truncated`.
6. Returns a result with the path, both hashes and saved times, the added and removed line counts,
   `coarse`, `truncated`, the unified `diff`, and `editorUrl` built with
   `PageUrls.EditorDiff(path, from.Hash, to.Hash)`.

The JSON envelope is the executor's ordinary `{ ok, data, error }`. The summary line is
`Compared 2 revisions of /blog/post`, which is what the transcript shows while the run streams.

The prompt gains a short `## Diff` section and moves to `page-author-v17`:

> `diff_page(path, fromHash, toHash)` compares two retained revisions and returns a unified diff,
> the two hashes and the editor link. With no hashes it compares the current revision with the one
> before it. Use it to answer "what changed" or to review your own edit; it writes nothing and needs
> no approval. The unified diff is data, not markup, and the link opens the editor's rendered view.

The model can paste `editorUrl` into its reply because the restricted Markdown profile allows
site-relative links. That is the only place the rendered diff reaches the assistant's reader, and it
is the same editor view the owner can open by hand.

### D5 — The editor's history view

`WriteHistoryAsync` keeps its table and gains a compare form above it when there are at least two
revisions:

```html
<form method="get" action="?" class="revision-compare">
  <input type="hidden" name="cmd" value="diff">
  <label for="diff-from">Compare</label>
  <select id="diff-from" name="from">…</select>
  <label for="diff-to">with</label>
  <select id="diff-to" name="to">…</select>
  <button type="submit">Compare revisions</button>
</form>
```

- The form is a GET with `cmd` hidden, because a GET form replaces its action's query string; the
  result is `/edit/key?cmd=diff&from=…&to=…`. A comparison is a read, so it needs no antiforgery
  token and no session.
- Each option's value is the full 64-character hash and its text is the saved time, a short hash and
  `Current` when it is the head. The default is the newest revision as `to` and the one before it as
  `from`, which is the comparison a reader wants after a save.
- With one revision the form is replaced by "Only one revision is saved; there is nothing to compare
  yet." With none, the existing "No saved revisions." remains.
- `WriteHistoryAsync`'s shell call gains `stylesheets: ["/editor.css"]`, so the form has its sheet;
  the diff view links the same sheet for its heading and the renderer's table.

`EditorEndpoint` gains a `diff` command on GET only (`IsAllowedCommand`), and
`TryGetRevisionPair` validates exactly one `from` and one `to`, each a 64-character SHA-256 hex
value. A malformed pair is the existing 400 error shape with "Back to history" and "Back to editor"
remedies; a well-formed hash that is not a revision of this page is the ordinary 404, matching
`cmd=revision`. `HandleAsync` gains a `DiffRenderer diff` parameter beside `PageEditor`, so the
minimal API resolves the singleton; the endpoint tests' direct calls go through one helper and gain
the renderer in one place.

`WriteDiffAsync`:

1. Reads the head; no head means 404. A tombstone still has a head, so its retained history can be
   compared.
2. Loads both `PageRevisionTemplate`s and checks `revision.Path == path`.
3. Starts a shell document titled "Compare revisions", with `/editor.css`, the site navigation and a
   menu of "Back to history" and "Back to editor".
4. Writes a heading, a line naming each revision as a link to its `cmd=revision` page with its saved
   time, the deleted-page sentence when the head is a tombstone, and the renderer's HTML.
5. Writes no cache headers of its own; `ShellDocument` keeps the editor's `no-store` rule.

`PageUrls` gains the matching `EditorDiff(PagePath path, string from, string to)`, so the assistant
tool and the tests do not hand-build the query.

### D6 — Bounds and failure behaviour

| Case | Behaviour |
| --- | --- |
| Identical templates, or the same hash twice | `p.diff-empty`, "No changes."; 200, not an error |
| A malformed or missing hash in the editor | 400, naming the link rule and offering the history |
| An unknown hash, or a hash from another page | 404, the same as `cmd=revision` |
| One revision and no second hash in the tool | Tool failure naming the page and the reason; nothing is read twice |
| A pair over `MaxEditDistance` or `MaxLines` | Correct coarse diff; `IsCoarse` is true and the HTML says why |
| A unified diff over the tool budget | Truncated text with `truncated: true`; the hashes and counts remain |
| A null argument to `ToHtml` | `ArgumentNullException`, as `IMarkdownRenderer.ToHtml` does |
| A page that calls the renderer | Pure and bounded; the render's default 5-second budget and body cap still apply |

A computed diff is never a revision or a separate diff row, and no source line reaches a log line or
a metric tag. The editor writes it only to the authenticated response; the assistant's result is
stored as an ordinary transcript message, which is how every tool result is kept. No new metric is
needed for a pure function.

## What this touches

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Diff/DiffLineKind.cs`, `DiffLine.cs`, `DiffHunk.cs`, `DiffDocument.cs` | The values |
| `src/uk.osric.asp.Core/Diff/LineDiff.cs` | Split, trim, Myers, hunks, numbering |
| `src/uk.osric.asp.Core/Diff/UnifiedDiff.cs` | The unified text for the assistant |
| `src/uk.osric.asp.Core/PublicAPI.Unshipped.txt` | The new public values and statics |
| `src/uk.osric.asp.Sdk/IDiffRenderer.cs` (new) | The page capability |
| `src/uk.osric.asp.Sdk/PublicAPI.Unshipped.txt` | `ToHtml` |
| `src/uk.osric.asp.Runtime/DiffRenderer.cs` (new) | Escaped semantic HTML; the CSS classes |
| `src/uk.osric.asp.Runtime/PublicAPI.Unshipped.txt` | `DiffRenderer`, `ToHtml` and `ToUnifiedDiff` |
| `src/uk.osric.asp.Web/Pages/PageEnvironment.cs` | `DiffRenderer Diff` |
| `src/uk.osric.asp.Web/Pages/PageChain.cs` | Offer `IDiffRenderer` to every layer |
| `src/uk.osric.asp.Web/Program.cs` | Register the singleton and pass it to the environment |
| `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs` | `cmd=diff`, the compare form, `WriteDiffAsync`, `TryGetRevisionPair`, the GET allow-list |
| `src/uk.osric.asp.Web/Http/PageUrls.cs` | `EditorDiff` |
| `src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs` | `diff_page`, its spec, handler and result; the `DiffRenderer` dependency |
| `src/uk.osric.asp.Web/Assistant/PageAuthorPrompt.cs` | The Diff section; `page-author-v17` |
| `src/uk.osric.asp.Web/Pages/Seeds/help` | The `IDiffRenderer` section, beside Markdown |
| `src/uk.osric.asp.Web/Pages/Seeds/layout.css` | `.diff-scroll` and its focus ring |
| `src/uk.osric.asp.Web/Pages/Seeds/site.css` | The `.diff` presentation |
| `src/uk.osric.asp.Web/Pages/Seeds/editor.css` | `.revision-compare` |
| `test/uk.osric.asp.Core.Tests/LineDiffTests.cs`, `UnifiedDiffTests.cs` (new) | The algorithm and its text |
| `test/uk.osric.asp.Runtime.Tests/DiffRendererTests.cs` (new) | Escaping, markup, empty and coarse |
| `test/uk.osric.asp.Web.Tests/PageDiffRenderTests.cs` (new) | A compiled page using the capability |
| `test/uk.osric.asp.Web.Tests/PageToolExecutorDiffTests.cs` (new) | The tool's defaults, hashes and refusals; the two existing executor fixtures gain `new DiffRenderer()` |
| `test/uk.osric.asp.Web.Tests/EditorEndpointTests.cs` | The compare form, the diff view and its errors |
| `test/uk.osric.asp.Web.Tests/PageAuthorPromptTests.cs` | The Diff section and v17 |
| `test/uk.osric.asp.Web.Tests/ShellViews.cs` | A `diff` view, so the accessibility rules see the table |
| `test/uk.osric.asp.Web.Tests/PageHarness.cs`, `PageEndpointTests.cs`, `PageLayoutChainTests.cs` | The `Diff` value in their environments |
| `test/uk.osric.asp.Web.BrowserTests/EditorFlowTests.cs` | Save twice, compare, see the removed and added lines |
| `docs/pages.md`, `docs/assistant.md`, `docs/storage.md`, `docs/glossary.md`, `docs/testing.md` | The capability, the tool, the derived nature, the term and the suites |
| `docs/decisions.md` | DEC-217–DEC-219 |

No database migration, no new setting and no change to `appsettings.json`. The caps are constants in
`Core` until a measurement argues for a setting; a diff is pure and the page render already has a
deadline and a body cap.

## Considered and rejected

| Option | Why not |
| --- | --- |
| `diff` as a child process | A runtime dependency the container does not promise, source on a pipe, and a parser per platform. It is also the one thing this change was asked not to do |
| A diff NuGet package | The algorithm is small and testable; DEC-15 wants a justification a dependency does not have here |
| Character- or word-level diff | More markup, more accessibility rules and a second renderer for a change the line markers already convey |
| A side-by-side view | Two tables or a wider grid for the same data; unified is the compact form and the one the assistant can read as text |
| A `diff_text` tool | It would let the model diff arbitrary strings, including its own output, and double the argument budget for no page-history question it cannot answer today |
| Rendering the diff as HTML in the transcript | Tool results are untrusted data shown in a `<pre>`; a second markup sink would need its own escaping and would make model-adjacent content renderable |
| A stored page for the viewer | History browsing is the editor's; a page would need the same storage reads and would be outside the assistant's reach, which is the ownership problem WHIM-02 names. The renderer is the reusable part |
| A route that pages fetch to get a diff | A page can already read both revisions with `IPageCatalog` and render them; a route would be a second contract with its own URL and access rules |
| Adding `Diff` to `IContext` | It is not something every page has to use; `Features` is the seam for a renderer the host may offer (DEC-142, DEC-148) |
| A configurable context or work cap | The defaults are constants with a clear bound; a setting would be a knob nobody needs until a measurement says otherwise |
| Storing a computed diff beside a revision | A diff is derived from two templates and would go stale or disagree with them; the templates are the authority, and the only stored copy is the assistant's ordinary tool result |

## Components

| Component | Responsibility | Where |
| --- | --- | --- |
| `DiffLineKind`, `DiffLine`, `DiffHunk`, `DiffDocument` | The immutable result: kinds, line numbers, hunks, counts and the coarse mark | `Core.Diff` |
| `LineDiff` | Split, trim, bounded Myers, hunk grouping and numbering | `Core.Diff` |
| `UnifiedDiff` | The canonical unified text | `Core.Diff` |
| `IDiffRenderer` | The page contract: two strings in, escaped HTML out | `Sdk` |
| `DiffRenderer` | The semantic table, the hidden markers, the empty/coarse sentences, and the unified text form | `Runtime` |
| `PageEnvironment`, `PageChain` | Offer the renderer to every page layer | `Web.Pages` |
| The `diff_page` spec and handler | Resolve two revisions of one page, default the pair, format and bound the text | `Web.Assistant` |
| The history compare form and `WriteDiffAsync` | Two hashes on a GET, two retained templates, one rendered table | `Web.Editor` |
| `.diff`, `.diff-scroll`, `.revision-compare` | The site's default look and the editor's form | the three seeded sheets |

## Tests

The order is the repository's: the pure rule first, then the renderer, then the composed host.

**`Core.Tests`**

- `LineDiffTests` — an empty pair, identical text, a pure addition, a pure removal, a replacement,
  common prefixes and suffixes, a change at the first and last line, CRLF and LF comparing equal, a
  trailing newline not adding a line, tabs and non-ASCII text preserved, hunk context of three and
  the merge of changes six lines apart, correct `0,0` headers for an empty side, and the coarse
  fallback past `MaxEditDistance` / `MaxLines`.
- `UnifiedDiffTests` — the exact `@@ -a,b +c,d @@` text and `' '`, `'-'`, `'+'` prefixes for a known
  case; an empty document writes nothing; the text round-trips through the same hunk values.

**`Runtime.Tests`**

- `DiffRendererTests` — `<`, `>`, `&`, quotes and a script-looking source line are encoded; the table
  has a head row and `scope="col"`; added and removed rows carry their marker and hidden word; the
  hunk row spans the table; an empty diff is `p.diff-empty`; a coarse diff includes its sentence; no
  inline style is written; a null argument throws.

**`Web.Tests`**

- `PageDiffRenderTests` — a compiled page gets `IDiffRenderer` and writes `ToHtml` raw; the rendered
  body contains the escaped source and the table; a page that passes visitor text cannot inject
  markup.
- `EditorEndpointTests` — the history page lists the two-select form with the previous revision as
  `from` and the current one as `to`; one revision explains there is nothing to compare;
  `?cmd=diff&from=&to=` renders the removed and added lines, includes `/editor.css`, compiles nothing
  and writes nothing; a malformed pair is 400; an unknown or wrong-page hash is 404; the same hash
  twice is "No changes." The file's direct `HandleAsync` calls gain the renderer through one helper.
- `PageToolExecutorDiffTests` — the definitions include `diff_page`, it is read-only; no hashes
  compares the newest two; explicit hashes select another pair; a hash from another page is refused;
  a page with one revision and no hashes is a failure; a shell file is refused; a too-large result is
  truncated with the flag.
- `PageAuthorPromptTests` — the prompt has the Diff section and the version is `page-author-v17`.
- `ShellViews` gains a `diff` view and the accessibility rules see its table; `PageHarness` and the
  other environments gain `Diff`.

**`Web.BrowserTests`**

- `EditorFlowTests` — save two different templates, open history, choose the two revisions, press
  Compare revisions, and see both the removed and the added text in the diff table. One test, no new
  host.

**Not changed.** `Web.IntegrationTests` gains nothing: the editor round-trip and routing are already
covered, and the unit tests drive the same endpoint with the real compiler. The provider is not
involved, so no live test is needed.

**The gate.** `make check` (format, licence, audit, build, test), then `make test-browser` once for the
new browser test, and a relative-link check over `docs/` after the documentation changes.

## Phases

Each phase ends with a green `make check` and its tests written first.

### Phase 0 — decisions and the pure diff (half a day)

- Record DEC-217–DEC-219 in [decisions](../../decisions.md).
- Write `DiffLineKind`, `DiffLine`, `DiffHunk`, `DiffDocument`, `LineDiff` and `UnifiedDiff` against
  their failing tests.
- Add the Core public-API entries with the analyzer, as the build requires.

**Done when** the algorithm and the unified text are proved by tests and nothing in `Web` uses them
yet.

### Phase 1 — the capability and the renderer (one day)

- `IDiffRenderer` in `Sdk`, `DiffRenderer` in `Runtime`, and their public-API entries.
- `PageEnvironment`, `PageChain`, `Program` and the test harnesses.
- `site.css` and `layout.css` defaults; the seeded `/help` section; the page author prompt's Diff
  section and `page-author-v17`.
- `PageDiffRenderTests`, `DiffRendererTests` and the shell accessibility view.

**Done when** a compiled page can render an escaped diff, the accessibility rules pass on its table,
and a page cannot reach `Core.Diff` directly (the existing page policy proves it).

### Phase 2 — the editor view (one day)

- `EditorEndpoint`'s `diff` command, the compare form and `WriteDiffAsync`.
- `PageUrls.EditorDiff` and `editor.css`.
- The endpoint tests and the browser test.

**Done when** the owner can choose two revisions on the history page and see the change, and every
bad link is a 400 or a 404 with a way back.

### Phase 3 — the assistant tool (half a day)

- The `diff_page` entry, its handler and its tests.
- The prompt's tool rule and the version assertion.
- `diff_page` returns text plus the editor link; a hash from another page and a one-revision page are
  refusals, not exceptions.

**Done when** a scripted run can compare the newest two revisions and paste the editor link, and the
tool changes nothing.

### Phase 4 — documents and the gate (half a day)

- `docs/pages.md`, `docs/assistant.md`, `docs/storage.md`, `docs/glossary.md` and `docs/testing.md`.
- The decision rows and the plan status.
- `make check`, the browser test, and the link check.

**Done when** the docs describe what the code does and the gate is green.

## Documentation and decisions

- **DEC-217** — The diff is the site's own line-based implementation. `Core.Diff.LineDiff` trims the
  common prefix and suffix, runs a bounded Myers diff with three lines of context, normalises line
  endings, and past its work cap shows the middle as one removed block and one added block rather
  than failing or calling a process. `UnifiedDiff` writes the same result as text.
- **DEC-218** — `IDiffRenderer` is a pure page capability like `IMarkdownRenderer`, reached through
  `ctx.Features`; it escapes every source line and writes semantic, inline-style-free markup, so a
  page may pass visitor or fetched text. The assistant gets the renderer's `ToUnifiedDiff` form as a
  read-only `diff_page` tool returning unified text and the editor link, not HTML in the transcript.
- **DEC-219** — The editor compares two retained revisions on a GET
  (`cmd=diff&from=<hash>&to=<hash>`); both hashes are validated as 64 hex characters and must belong
  to the page, the view compiles and writes nothing, and a malformed link is a 400 while an unknown
  revision is a 404.

Docs changes: `pages.md` moves the capability count from seven to eight and adds the
`IDiffRenderer` bullet; `assistant.md` adds `diff_page` to the tools and the editor link to the
transcript paragraph; `storage.md` says a computed diff is not its own row or revision and the tool
result is a transcript message; `glossary.md` gains **Diff**; `testing.md` names the new suites and
the browser test; the seeded `/help` teaches the capability; the prompt moves to
`page-author-v17`. WHIM-02's line is removed when the work lands, because the plan is the shape it
grew. If another branch takes DEC-217–DEC-219 before this lands, the block moves to the next free
numbers and every citation follows, as the repository's merge rule requires.

## Risks

| Risk | Mitigation |
| --- | --- |
| **A page makes the renderer expensive.** | `MaxEditDistance`, `MaxLines` and prefix/suffix trimming bound the work; the existing render budget (five seconds by default) and body cap still surround the call; the fallback is correct rather than a failure |
| **A page treats the diff as a sanitiser.** | It does escape its input, unlike Markdown; that is what makes visitor and fetched text safe. The interface, `/help` and the prompt say which rule belongs to which renderer |
| **The assistant's tool result is hostile.** | It is a tool result, not markup: the transcript escapes it, the model reads it as data, and the link is site-relative. No raw HTML is produced |
| **A hash from another page is spent.** | Both revisions are loaded and their `Path` is checked against the requested page; the editor and the tool use the same rule |
| **The editor view becomes a second history implementation.** | It reuses `PageEditor.ListRevisionsAsync` and `GetRevisionTemplateByHashAsync` and the page's own history; it does not read the database or compile |
| **The diff drifts from the editor's labels.** | The renderer owns the comparison and the editor owns the revision labels; the tests pin the markup, the links and the empty/coarse sentences |
| **The Sdk change invalidates stored images.** | Expected: `RuntimeId` covers the Sdk module id, so images recompile lazily. No migration and no data change |
| **The plan's `MaxEditDistance` is wrong.** | It is a constant with a test at the boundary; a real page edit is far below it. If a measurement says otherwise, the value moves in one place |

## Delivered when

- A page calls `ctx.Features.GetRequired<IDiffRenderer>().ToHtml(before, after)` and gets an escaped,
  accessible table; Markdown's raw-HTML rule does not leak into it.
- The assistant's `diff_page` compares the newest two revisions by default, accepts explicit hashes,
  refuses another page's hash and a one-revision page, and returns a unified diff plus the editor
  link without writing anything.
- The editor's history page has the two-select compare form; choosing two revisions shows the
  removed and added lines, and a malformed or foreign link is a 400 or a 404 with a way back.
- An empty diff says "No changes." and a too-different diff says why it is coarse, in the same
  places a page and the editor read.
- `Core.Tests`, `Runtime.Tests`, `Web.Tests` and the one browser test cover the algorithm, the
  escaping, the wiring, the tool and the view; `make check` passes.
- `docs/pages.md`, `docs/assistant.md`, `docs/storage.md`, `docs/glossary.md` and `docs/testing.md`
  describe what the code does, and DEC-217–DEC-219 are in `docs/decisions.md`.
