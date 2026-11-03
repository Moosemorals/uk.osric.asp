<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# BUG-16 — Some site markup is not semantic HTML

| | |
| --- | --- |
| Severity | Low |
| Size | M |
| Area | Web, accessibility |
| Location | `src/uk.osric.asp.Web/Editor/EditorEndpoint.cs`, shared page layouts and endpoints |

## Problem

Some rendered pages use generic elements, such as paragraphs around groups of navigation links,
where HTML provides a semantic landmark. Inconsistent semantics make page structure harder for
assistive technology to expose and harder for users to navigate. Fixing the editor's link groups
is a focused improvement, but it does not establish that the rest of the site is semantically sound.

## Fix outline

1. Audit rendered site HTML, including the shared layout, editor, login, status, error and
   confirmation views, against the HTML semantics and accessibility behavior they intend to expose.
2. Replace generic wrappers only where a semantic element is appropriate: navigation groups use
   `<nav>` with a concise accessible name when there are multiple navigation landmarks; main
   content, headings, lists, forms, labels, buttons and tables use their elements according to
   their actual roles. Do not turn ordinary prose into landmarks or add redundant ARIA roles.
3. Keep visual behavior unchanged unless a separate design change is needed. Encode user-provided
   text and attributes as before.
4. Add focused markup assertions for landmarks, form labels, heading structure and table headers;
   use an automated accessibility check only if it can exercise the server-rendered pages without
   introducing a browser test for behavior already covered by unit or integration tests.
5. Run the existing unit and integration suites and review the rendered pages with a screen reader
   or accessibility tree before closing the job.
