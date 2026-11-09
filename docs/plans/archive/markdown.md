<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Markdown for pages and the assistant

Status: **implemented**. Written and built 2026-11-09.

## The short version

- **Markdig 1.6.0 belongs to `Runtime`.** It is the host's Markdown parser, beside the ECMAScript
  parser, and it is the only assembly that references it. A page never names Markdig; it names the
  Sdk capability.
- **A page gets `IMarkdownRenderer`.** `ctx.Features.GetRequired<IMarkdownRenderer>().ToHtml(source)`
  returns HTML the page writes with `<%== %>`. The page profile is CommonMark plus pipe tables and
  auto-links, and raw HTML passes through: it is the author's renderer, not a sanitiser.
- **Assistant replies are Markdown by default.** There is no `render_markdown` tool. The transcript
  renders the stored assistant message with a narrower profile: no raw HTML, images, autolinks or
  unsafe schemes; headings flattened to `h3`; pipe tables; site-relative or `http(s)` links with
  `rel="nofollow noopener noreferrer"`.
- **A live step becomes formatted when it completes.** `assistant.js` keeps streaming deltas as
  plain text, then asks an authenticated content route for the server-rendered HTML of the stored
  message. The reload after the run uses the same renderer, so the two cannot disagree.
- **Tables are the site's own renderer.** Markdig writes alignment as an inline `style` attribute
  and `<th>` without a scope. The site's CSP forbids the first and the shell's accessibility rules
  require the second, so `AccessibleTableRenderer` ignores alignment and writes `scope="col"`.

## Why these choices

### Markdig in Runtime

DEC-15 asks a third-party package to earn its place. Markdig is a parser with no native asset and no
I/O; it does not execute anything. `Runtime` already holds the host's other parser and is where
"parse this text into a value" belongs, so the package is referenced there and nowhere else. `Core`
stays reference-free; the rule that the restricted renderer needs is a pure string rule and lives in
`Core.Markdown.LinkPolicy`.

### A capability, not more IContext

DEC-142 and DEC-148 settled the seam: something a render may offer is read from
`ctx.Features`, and `IContext` grows only for what every page always has. Markdown is offered by
every render today, but it is still a capability rather than a context member; a page that never
wants it never meets it, and the page contract stays small. The same handle reaches a layout layer,
because `PageChain` registers the capability for each layer.

### Pages may pass raw HTML through

A page author can already write HTML in the template and `<%== %>` any string. Stripping raw HTML
from Markdown would not add a boundary, and it would surprise an author who expects Markdown to
work like Markdown. The cost is stated where it matters: `IMarkdownRenderer`'s docs, `/help` and the
assistant prompt all say the renderer is not a sanitiser and that visitor or fetched text must be
escaped with `ctx.Write` instead.

### The assistant gets a second profile

Model output is influenced by the page source it reads, so it is not the author. The restricted
profile disables raw HTML and the autolink parser, replaces the link renderer with one that asks
`LinkPolicy`, drops an image to its alt text, flattens headings, and uses the site's table renderer.
That is one renderer with two profiles, so escaping, URLs and table markup have one owner.

Rejected:

- **A `render_markdown` tool only.** The transcript would stay plain text, which is the experience
  the change exists to improve, and every reply would cost a step.
- **Both a tool and default rendering.** The page capability already lets a page render Markdown;
  a tool would duplicate it without a reader.
- **Client-side Markdown in `assistant.js`.** A second parser with its own escaping and URL rules
  would drift from the server's. The live transcript asks the server instead.
- **One profile for both.** A page author is trusted with raw HTML; model output is not.
- **Markdig's default table renderer.** It writes inline styles the CSP blocks and header cells
  without a scope the accessibility rules require.

## Where the code is

- `Sdk/IMarkdownRenderer.cs` — the page capability.
- `Core/Markdown/LinkPolicy.cs` — the pure URL rule for the restricted profile.
- `Runtime/MarkdownRenderer.cs` — the two pipelines and the per-call renderer.
- `Runtime/RestrictedLinkRenderer.cs`, `RestrictedHeadingRenderer.cs`, `AccessibleTableRenderer.cs`
  — the restricted link and heading rules and the shared table markup.
- `Web/Pages/PageEnvironment.cs`, `PageChain.cs` — the capability every page layer offers.
- `Web/Assistant/AssistantEndpoint.cs` — the transcript render and the content route.
- `Web/Pages/Seeds/assistant.js`, `assistant.css` — the live swap and the rendered look.

## Verification

- `Core.Tests/MarkdownLinkPolicyTests` — every allowed and refused URL shape.
- `Runtime.Tests/MarkdownRendererTests` — both profiles, the table renderer, raw HTML, images,
  unsafe links, headings, autolinks, fenced code and null input.
- `Web.Tests/PageMarkdownRenderTests` — a real compiled page using the capability.
- `Web.Tests/PageScopeTests` — a page may not use `Markdig`.
- `Web.Tests/AssistantTranscriptTests`, `AssistantMessageContentTests` — the transcript and the live
  route, including hostile Markdown and ownership.
- `Web.Tests/ConversationStoreTests` — `GetMessageAsync` is scoped by owner and conversation.
- `Web.Tests/AccessibilityTests` — the assistant fixture now carries a rendered heading and table,
  so the shared rules see the `h3` and `scope="col"`.
- `Web.IntegrationTests/AssistantChatLayoutTests` — a scripted run, the content route through the
  real host, and the reloaded transcript.
- Browser suite: the existing chat tests still find a non-empty `.assistant-content`, and
  `AMarkdownReplyIsFormattedWhenTheStepCompletesAsync` proves a scripted reply becomes a `<strong>`
  and a site link after `message.completed`.

What a string cannot show — the table's reading order, the rendered code block's contrast, the
change of a live message from plain text to formatted HTML with a screen reader — is the hand pass
recorded with the accessibility work (DEC-110), not a claim made here.
