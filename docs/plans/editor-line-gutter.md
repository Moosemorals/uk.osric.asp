<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# The editor's line gutter, linked diagnostics and its script

Implementation plan for [IDEA-001](../jobs/IDEA-001.md) steps 2 and 4 — a server-rendered line
gutter, each diagnostic anchored to its line, and the first script on the site with the policy that
goes with it. Three things ride along because they touch the same markup: the editor fills the
viewport, the caret survives a save, and the two inline `<pre style>` attributes go.

## Status

Planned.

## Goal and constraints

- One line number per source line, aligned with the text, live as the author types and deletes.
- Each diagnostic with a position is a link that scrolls its line into view and marks it.
- The textarea stays the form field, keeps its native resize handle, and the save path is untouched:
  an antiforgery `POST`, then `303` back to the editor with a flash message.
- The editor fills the viewport between the page header and the footer, and the caret and selection
  survive a save.
- Long lines scroll horizontally; `wrap="off"` keeps the line mapping exact.
- No `unsafe-inline`, no `<style>`, no inline script: styling through stylesheets and CSSOM, script
  through a native module.
- One security-header set on every response, with `script-src 'none'` everywhere but the editor, and
  a defined way for a page to replace its policy later.

## Why the gutter is scripted

CSS cannot count the lines of a textarea. A counter increments per element, and the textarea's value
is not in the DOM — the browser creates no node per line. `grid-auto-rows` sizes items that exist, so
the items still have to come from somewhere. The only CSS that reacts to the value is
`field-sizing: content`, which resizes the box and fights the resize handle.

So the number of lines is only knowable in script, and the split of work follows: **the server
renders the first gutter** — correct with no script, and what gives diagnostic links a target on the
redirected `GET` before anything runs — and **the module keeps it true** while the author types.

## Decisions

### 1. Core owns the line count, Web owns the HTML

`TemplateLines` becomes `public` with `public static int Count(string)`; `PositionOf` stays
`internal`, because only the tokenizer and generator need it. Its rule — `\r\n`, `\n` and lone `\r`
are breaks — is already the one rule the tokenizer and the generator share, and the gutter is a
third caller of the same rule. The doc comment gains that caller.

The anchor id and the gutter markup are HTML, so they stay in `Web`: `PageLayout.Gutter(int)` and a
`private static string LineAnchor(int)` that both the gutter and the diagnostics use, so producer and
consumer of `#L{n}` cannot drift.

The client counts with `value.split('\n')`, which is not a second rule: the textarea value
sanitiser has already replaced CR and CRLF with LF, and a POSTed textarea never carries a CR. The
server-side count still handles stored pages that arrived another way.

### 2. The gutter is a decorative `<div>`, not a `<nav>`

```html
<div class="gutter" id="gutter" aria-hidden="true"><span id="L1">1</span><span id="L2">2</span></div>
```

- **Not `<nav>`.** `nav` is a landmark for navigation links. The gutter is a column of numbers, not a
  way around the site, and the editor page already has two real navs — page navigation and page
  actions. A third landmark for line numbers is noise for anyone navigating by landmark. If the
  numbers ever become clickable they still are not navigation: the shape would be a list of buttons,
  not a nav.
- **Not `<ol>`.** An ordered list is the closest description of "numbered lines", but the container
  is `aria-hidden`, so the list semantics are never exposed; it would buy a `list-style` reset and
  nothing else.
- `aria-hidden`, because the textarea already holds the text and "list of 300 items" in the reading
  order is noise. The diagnostic link text carries the position in words, so nothing is lost when the
  numbers are hidden.
- A consequence to accept: `href="#L12"` targets an element inside an `aria-hidden` subtree. That is
  unusual but valid — the link's own text ("line 12, column 5: error CS…") is what a screen reader
  reads, and activating it still scrolls.
- `id` on each span, because `href="#L12"` both scrolls and paints the number through `:target`. No
  whitespace between the spans — one number per row, built with `string.Concat`.

### 3. The textarea is untouched as a form field

`rows="10"`, `cols="50"`, the native resize handle, and `wrap="off"`. No computed rows: that was the
first draft's mistake, because it only fits the content the server saw, and it needs `resize: none`
to stay aligned.

Alignment comes from the wrapper instead: `.editor` is a row flex container, the textarea stretches
to its height, the gutter mirrors `scrollTop`. The textarea is `flex: 1 1 auto` rather than `flex: 1`:
a flex basis of `0` would ignore a width the author dragged, while `auto` lets the inline width and
height the browser writes win over the layout. The handle keeps doing what it does today, sideways
as well as down.

`wrap="off"` is the one constraint: a soft-wrapped line is several visual rows and no gutter can
follow it without measuring every line. Horizontal scrolling inside the textarea is accepted.

### 4. The editor page fills the viewport

Flex computes the header and footer rather than arithmetic, because the header is "whatever is
there" — an `h1` that wraps, a flash message, two navs, a diagnostics block:

```css
body.editor-page {
    box-sizing: border-box;
    min-height: 100vh;   /* fallback */
    min-height: 100dvh;  /* dvh, so a mobile URL bar does not add a scrollbar */
    display: flex;
    flex-direction: column;
}
```

`min-height` and not `height`: a long diagnostics block then scrolls the page instead of crushing
the editor, and `.editor { min-height: 12rem }` keeps the box usable when it does. The sign-out form
and the footer sit at the bottom of a short page, which is what "minus the header and footer" means.

`PageLayout.Header` gains a body-class parameter so only the editing view opts in: the history,
generated and revision views also link `/editor.css`, for `.source`, without taking the layout.

### 5. Diagnostics stay a `<pre>`; positioned ones are links

```html
<pre class="diagnostics"><span class="diagnostic-error"><a href="#L2">line 2, column 5: error CS0103: …</a></span>
<span class="diagnostic-error">error PAGE001: …</span>
</pre>
```

```csharp
public static string Diagnostics(IReadOnlyList<PageDiagnostic> diagnostics, bool linkToLines = false);
```

One renderer, one opt-in flag; the error pages call it exactly as they do today, byte for byte. A
diagnostic with no position is never linked — it has no line to jump to. Rejected: moving the block
to `<ul>`, which is better semantics and BUG-16's business, but `.diagnostics` is styled in the
seeded `/site.css` page and seeds never overwrite a live page, so a new element would ship unstyled
on every existing install.

### 6. The script and the stylesheet are seeded pages

`/editor.css` and `/editor.js` are new keys in `Pages/Seeds`, so an existing database gets them on
the next boot — whereas editing the `site.css` seed delivers nothing to a site that already has
`site.css`. Both set their own content type and `Cache-Control: no-cache`, exactly as `/status.css`
does. `/editor.js` is `text/javascript; charset=utf-8`.

`/editor.js` is loaded as `<script type="module">`: native ES modules, no bundler, no npm, no
transpiler. Modules are deferred, so position in the document does not matter. It must contain no
`<%`, which would start template code in a page whose whole body is literal text.

Editing the script in the editor is a feature, not an accident: break it and the textarea and form
still work.

### 7. One middleware sets the security headers; only the editor allows script

```
Content-Security-Policy: default-src 'none'; script-src 'none'; style-src 'self'; img-src 'self';
  connect-src 'self'; form-action 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Resource-Policy: same-origin
Referrer-Policy: same-origin
Permissions-Policy: accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(),
  microphone=(), payment=(), usb=()
X-XSS-Protection: 0
```

- `X-Frame-Options: DENY` beside `frame-ancestors 'none'`: the directive is ignored by a few old
  engines, and nothing frames the site.
- `Referrer-Policy: same-origin`: a page linked from this site sees the full editor URL, and no
  referrer leaves the site at all. The small internal leak is wanted — it keeps same-site navigation
  readable — while an outbound link still tells the other end nothing.
- `Cross-Origin-Opener-Policy` and `Cross-Origin-Resource-Policy` are safe here precisely because
  everything is same-origin: nothing opens a window, and nothing should be hotlinkable.
- `Permissions-Policy` is a default-deny for device APIs, so a page that wants one is a decision.
- `X-XSS-Protection: 0`: with a CSP the advice is to disable the legacy auditor rather than let it
  rewrite the DOM. It only affects old browsers.
- Not set, on purpose: `Cross-Origin-Embedder-Policy` (only earns anything with cross-origin
  isolation, and it breaks cross-origin embeds), `Public-Key-Pins` and `Expect-CT` (dead), and
  `Clear-Site-Data` (nothing on logout needs it).
- `Strict-Transport-Security` is already `app.UseHsts()`. Its defaults are 30 days and no
  `includeSubDomains`; raising it commits every subdomain of the domain, so it is a deploy decision
  and not a side effect of this job.
- Kestrel still advertises itself; `AddServerHeader = false` would stop that, and Caddy passes the
  header through. Also a decision of its own.

The CSP is the one that varies by page. The editor's copy is the same string with `script-src 'self'`
in place of `'none'`. Both come from one helper so they cannot drift, and a unit test asserts they
differ in exactly one directive:

```csharp
internal static class SecurityHeaders {
    public const string ContentSecurityPolicy = "Content-Security-Policy";

    public static string Default => Policy("'none'");
    public static string WithScripts => Policy("'self'");

    public static Task ApplyAsync(HttpContext context, RequestDelegate next);

    /// <summary>Widens this response's policy to allow same-origin scripts.</summary>
    public static void AllowScripts(HttpContext context);

    private static string Policy(string scriptSources) =>
        $"default-src 'none'; script-src {scriptSources}; style-src 'self'; img-src 'self'; " +
        "connect-src 'self'; form-action 'self'; object-src 'none'; base-uri 'none'; " +
        "frame-ancestors 'none'";
}
```

- **The default lives in middleware, and only the editor overrides it.** Putting the header in each
  endpoint would mean `EditorEndpoint`, `LoginEndpoint`, `StatusEndpoint`, `PageEndpoint`,
  `ErrorEndpoint`, `NotFound`, `Redirects`, health and metrics — and responses that no endpoint owns,
  such as a routing 404 or a 405, would get nothing. A default that cannot be forgotten plus one
  deliberate widening is fewer places to get wrong, not more.
- `WriteEditorAsync` calls `AllowScripts` before it writes anything; every other editor view — the
  listing, history, revision, generated, delete and scrub pages — keeps `script-src 'none'`, because
  none of them loads a script.
- The middleware registers `Response.OnStarting` and writes each header there **only if it is still
  absent**. Late enough that the endpoint's override is visible, and late enough to survive the
  exception handler, which clears the response before re-executing the error page. An error page
  therefore always gets the default policy, which is the one it wants.
- `nosniff` also forces `/editor.js` to be served with a correct type.
- `script-src 'self'` needs no nonce, because there is no inline script: this is stricter than
  IDEA-001 step 4 assumed.
- `style-src 'self'`, not `'none'`: every page links `/site.css`, and the status page adds its own.
- `style-src 'self'` blocks `<style>` and `style=` attributes, so the two
  `style="max-width:100%;overflow-x:auto"` attributes become `.source` in `/editor.css`, and those
  two views link it. Nothing else in the host writes an inline style.
- CSSOM is not affected: `element.style.height = …` from the module is allowed,
  `setAttribute('style', …)` is not. Dynamic styling goes through properties or a stylesheet.
- `connect-src 'self'` blocks an outbound fetch, which is correct until IDEA-002 makes it a
  deliberate change; `upgrade-insecure-requests` is left out so development over HTTP still loads
  its assets.
- A page that writes a `<script>` is now blocked, which is the point of `script-src 'none'`. When a
  page genuinely needs one, that is a policy decision: it sets the header itself before writing, and
  this helper grows a third policy rather than an `unsafe-inline`.

### 8. The caret survives a save

`submit` writes `{ start, end, scrollTop }` to `sessionStorage` under the editor's pathname; the
module restores it on load, clamps it to the value length and focuses the textarea. Storage failure
(private mode, quota) loses the caret and nothing else — a stale or unreadable entry restores
nothing. Per tab, so it cannot leak between windows, and consumed once so a later reload starts at
the top.

### 9. Rejected

- **JavaScript scroll-sync instead of a server gutter** — the links would have no target before the
  script runs, and the no-script page would lose its numbers entirely.
- **Reimplementing wrapping to keep numbers with soft wrap** — per-line measurement, a mirror
  element and a re-measure on every keystroke, for a mode most code still reads worse in.
- **`field-sizing: content`** — sizes the box, still no per-line hook, and overrides the handle.
- **CSS counters** — they would only remove `textContent` from spans the script already creates.
- **A fingerprint guard before linking diagnostics** — a save always recompiles, so a mismatch can
  only come from hand-edited storage. DEC-27.
- **Auto-jumping to the first diagnostic after a save** — needs script or a fragment on the
  redirect; the block is already above the form on the redirected `GET`.
- **Ace now** — a vendoring flow, a licence, a custom mode and a CSP decision for one feature. This
  script is the no-Ace fallback the spike describes, and it keeps the script surface at one file
  when the policy lands.

## Markup

```html
<body class="editor-page">
  …
  <form method="post" action="?" class="editor-form">
    <input type="hidden" name="__RequestVerificationToken" value="…">
    <div class="editor">
      <div class="gutter" id="gutter" aria-hidden="true"><span id="L1">1</span></div>
      <textarea name="content" id="content" rows="10" cols="50" wrap="off" spellcheck="false">
{encoded content}</textarea>
    </div>
    <button type="submit">Save</button>
  </form>
  <form method="post" action="/logout">…</form>
  <script type="module" src="/editor.js"></script>
</body>
```

The newline after the `<textarea …>` start tag is load-bearing: the HTML parser drops exactly one
newline there, so emitting one of ours stops it eating the author's. The same parser rule affects the
read-only `<pre>` in the revision and generated views, which show no line numbers and can take the
same fix separately.

## `/editor.css`

Native nesting, one level deep, because the rules for the editor box belong together and there is no
preprocessor to argue about. The caveat is that a browser without nesting support drops the whole
nested rule rather than degrading it, so nothing load-bearing may live inside one; the page-level
rules stay flat.

```css
body.editor-page {
    box-sizing: border-box;
    min-height: 100vh;
    min-height: 100dvh;
    display: flex;
    flex-direction: column;
}

.editor-page form.editor-form {
    display: flex;
    flex-direction: column;
    flex: 1;
    min-height: 0;
}

.editor {
    display: flex;
    flex: 1;
    min-height: 12rem;
    min-width: 0;
    border: 1px solid #ccc;
    background: #fff;
    font: 1rem/1.6 ui-monospace, Menlo, Consolas, monospace;

    & .gutter {
        box-sizing: border-box;
        overflow: hidden;
        padding: 0.5rem 0.5rem 0 0;
        text-align: right;
        white-space: nowrap;
        user-select: none;
        color: #767676;
        background: #f5f5f5;
        border-right: 1px solid #ddd;
    }

    & .gutter > span {
        display: block;
        min-width: 3ch;
    }

    & .gutter > span:target {
        background: #ffe08a;
        color: #000;
    }

    & textarea {
        flex: 1 1 auto;
        min-width: 0;
        margin: 0;
        padding: 0.5rem;
        border: 0;
        font: inherit;
        white-space: pre;
        background: transparent;
        resize: both;
    }
}

.diagnostics a {
    color: inherit;
    text-decoration: underline dotted;
}

.source {
    max-width: 100%;
    overflow-x: auto;
}
```

Right-aligned numbers: `text-align: right` on the gutter, with the column as wide as its widest
number, so 9 and 10 line up on the units digit and a four-digit page widens the column by itself.
Matching top padding and inherited `line-height` are what put the numbers on the text's rows;
`box-sizing: border-box` on the gutter is what lets the script clip it to the textarea's content box.

## `/editor.js`

```js
const textarea = document.getElementById('content');
const gutter = document.getElementById('gutter');
const editor = document.querySelector('.editor');
const storageKey = `editor:caret:${location.pathname}`;

// The textarea value sanitiser has already replaced CR and CRLF with LF, so LF is the only break.
const lines = () => textarea.value.split('\n').length;

function sync() {
    const wanted = lines();
    while (gutter.childElementCount < wanted) {
        const number = gutter.childElementCount + 1;
        gutter.append(Object.assign(document.createElement('span'),
            { id: `L${number}`, textContent: number }));
    }
    while (gutter.childElementCount > wanted) {
        gutter.lastElementChild.remove();
    }
}

function follow() {
    gutter.scrollTop = textarea.scrollTop;
}

function fit() {
    // Clipped to the content box, so no number hides behind the horizontal scrollbar.
    gutter.style.maxHeight = `${textarea.clientHeight}px`;
    // A height the author dragged is the size they asked for; otherwise the viewport decides.
    editor.style.flex = textarea.style.height ? 'none' : '';
}

function remember() {
    try {
        sessionStorage.setItem(storageKey, JSON.stringify({
            start: textarea.selectionStart, end: textarea.selectionEnd, scrollTop: textarea.scrollTop,
        }));
    } catch {
        // Storage can be unavailable (private mode, quota); losing the caret is not worth failing the save.
    }
}

function restore() {
    try {
        const saved = JSON.parse(sessionStorage.getItem(storageKey) ?? 'null');
        if (saved === null) {
            return;
        }

        sessionStorage.removeItem(storageKey);
        const length = textarea.value.length;
        textarea.setSelectionRange(Math.min(saved.start, length), Math.min(saved.end, length));
        textarea.scrollTop = saved.scrollTop;
        textarea.focus();
    } catch {
        // A stale or unreadable entry restores nothing.
    }
}

textarea.addEventListener('input', sync);
textarea.addEventListener('scroll', follow, { passive: true });
textarea.form.addEventListener('submit', remember);
new ResizeObserver(fit).observe(textarea);

sync();
fit();
follow();
restore();
```

`sync` adds and removes only the difference, so existing `id`s and a `:target` highlight survive
typing.

## Browser tests

The four things this job cares about — alignment, resize, caret restore and a clean console under
the policy — are invisible to NUnit. A Playwright suite is the only way to assert them rather than
look at them once.

**A separate project, `test/uk.osric.asp.Web.BrowserTests`, opt-in and out of `make check`.** The
precedent is `image-check`: it needs podman, so it lives in `ci` and not in `check`. Browser tests
need a browser download, so they get their own make target and join `make ci` when the full gate is
asked for. Keeping them out of `make check` matters — the gate is run by hand, and a suite that takes
a minute and can flake is a suite people stop running.

**Not inside `Web.IntegrationTests`.** That suite is `WebApplicationFactory` plus `HttpClient`, runs
in seconds, and substitutes nothing but the hasher. A browser engine is an external dependency with
its own version and download step; folding it into the same project makes the fast suite slow and
makes "the integration tests fail" mean two different things.

Shape: `Microsoft.Playwright.NUnit` (pinned in `Directory.Packages.props`), the real host started
in-process on a loopback port with a throwaway SQLite file, and sign-in through the real `/login`
form so the cookies are real. Scenarios worth writing, in order:

1. Typing two newlines and deleting one changes the number of gutter spans by the same amount, and
   the numbers stay right-aligned.
2. For lines 1 and 40 of a 50-line page, the gutter span's `boundingBox().top` equals the first
   row's top plus 39 line heights — the alignment claim, asserted rather than eyeballed.
3. Dragging the resize handle changes the textarea's box, keeps those boxes aligned, and leaves no
   number clipped by the horizontal scrollbar.
4. Clicking the first diagnostic's link makes that line the first visible row.
5. Saving restores the caret and the selection.
6. With scripts blocked, the gutter still matches the saved page and the form still saves.
7. No console error and no `securitypolicyviolation` on any of the above.

**Reqnroll: no.** It is a Gherkin layer over NUnit, and it pays off with non-technical stakeholders
and a large stable behaviour set. Here the stakeholder writes the C#, and there are seven scenarios:
step definitions would be an indirection that hides the selector under test and adds a place for a
step to drift from the page. NUnit names that read like those sentences document as much for none of
the cost. Revisit if the acceptance criteria ever become a contract with someone else.

The suite is its own job; this one records the shape and ships
[Verification](#verification) done by hand until it lands.

## Implementation sequence

Test-first throughout; one behaviour per test.

1. **Core line count** — `TemplateLines` public, `Count` public, `PositionOf` internal; tests for
   `""`, `"a"`, `"a\n"`, `"a\nb"`, `"a\r\nb"`, `"a\rb"`, `"\n\n"`. `make test-core`.
2. **Server gutter** — `PageLayout.Gutter(int)`, the `.editor` wrapper, `lineCount` from the stored
   content; tests: one number per line for each break style, ids `L1..Ln`, empty source shows one.
3. **Diagnostic links** — `Diagnostics(diagnostics, linkToLines)`; tests: every `href` has a
   matching `id`, an unpositioned diagnostic is plain, the error page is unchanged, order is still
   by line.
4. **`/editor.css` page** — layout and gutter rules, `.source`, `.diagnostics a`; the textarea gains
   `id`, `wrap`, `spellcheck`, the leading newline; the generated and revision views link the
   stylesheet and drop their inline styles; `PageLayout.Header` gains the body class; tests: link
   tags, body class only on the editor view, `/editor.css` served as CSS.
5. **Security headers** — `SecurityHeaders` with `OnStarting`, no inline styles left; tests: every
   header present on the editor view, on `/status`, on a 404 and on a `500`; `script-src 'none'`
   everywhere except the editor view; the two policies differ in exactly one directive; one value
   only; and a header an endpoint already set is not overwritten.
6. **`/editor.js` page** — module tag, content type, no `<%`; server tests for the tag and the
   served type. Then the browser checks below.
7. **Documentation and gate** — IDEA-001 notes steps 2 and 4 done; `docs/testing.md`'s "no browser
   needed" gains the caveat that the gutter, the resize behaviour and the caret are browser checks;
   the decisions table gains the policy row; `make check`.

## Tests that must change

- `EditorEndpointTests` in `Web.Tests` asserts the textarea markup verbatim; it gains the new
  attributes and the newline, which keeps it pinning the client experience down to the attributes.
- Five `</textarea>` assertions in `Web.IntegrationTests/EditorEndpointTests` gain the leading
  newline — which is the round-trip guarantee this work introduces.
- Any assertion of `<body>` gains `class="editor-page"` on the editor view only.

## Verification

Hand checks until the browser suite exists:

- Typing and deleting add and remove numbers on the right rows; scrolling keeps them aligned.
- Dragging the resize handle both ways keeps the numbers aligned, keeps the box sized to the drag,
  and hides no number behind the horizontal scrollbar.
- A four-digit page widens the gutter and keeps the numbers right-aligned.
- A 300-character line does not disturb the numbering.
- The textarea fills the space between header and footer at short and tall viewports, and shrinks
  with the window until it is dragged.
- Clicking a diagnostic lands on its line and marks the number.
- After Save the caret, selection and scroll position come back.
- With JavaScript disabled the numbers still describe the saved page, the links still land, and
  editing still saves.
- The console is clean under the policy, on the editor, the status page and a 404.

## Acceptance criteria

- One gutter number per source line for `\n`, `\r\n` and `\r` sources, including a trailing newline
  and an empty source, and the numbering follows typing without a reload.
- The numbers are right-aligned, and the column widens for four-digit pages.
- Every positioned diagnostic links to a target that exists; unpositioned ones are plain text; both
  appear in line order and the error page renders exactly as before.
- The textarea is still the form field, its native resize still works, the POST/`303` path is
  unchanged, and a failed save still shows the text with its diagnostics.
- A source beginning with a newline round-trips unchanged, and its gutter and its rows agree.
- The editor fills the viewport between header and footer, and the caret survives a save.
- Every response carries the header set in decision 7; `script-src 'none'` on every page except the
  editor view, which carries `'self'`; no inline script and no inline style anywhere, and
  `unsafe-inline` appears nowhere; `/editor.js` is a native module.
- `make check` passes.

## Follow-ups this deliberately leaves

- **Browser suite** — `test/uk.osric.asp.Web.BrowserTests` with Playwright and its own make target, as
  described above; it belongs with CI (OPS-03) rather than with this job.
- **HSTS settings** — `app.UseHsts()` runs with its defaults (30 days, no `includeSubDomains`).
  Raising it commits every subdomain of the domain, so it is a deploy decision; see
  [deploy/README.md](../../deploy/README.md).
- **`Server` header** — Kestrel advertises itself and Caddy passes it through;
  `AddServerHeader = false` removes it, whenever it is worth a line.
- **Ace** (IDEA-001 steps 1 and 3): this page is the fallback it enhances, and the annotation payload
  reuses `PageDiagnostic.Position` with `Line - 1`.
- **Per-page policies**: the middleware's set-if-absent rule is the hook; nothing needs it yet.
- **BUG-16**: the diagnostics block becomes a list when the semantic audit moves both call sites and
  the stylesheet together.
- **`<pre>` leading newline** in the revision and generated views.