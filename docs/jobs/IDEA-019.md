<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-019 — Small gaps in the page request, response and API policy

| | |
| --- | --- |
| Size | Small |
| Area | Sdk, Core, Web |
| Location | `src/uk.osric.asp.Sdk/PageRequest.cs`, `src/uk.osric.asp.Core/Compilation/PageApiPolicy.cs` |

## Idea

Three gaps, found together while asking what a shell view would need to become a page. They are
independent and each is a sitting; they are one job because none of them is worth a file of its own
and together they are what a page-authoring session keeps tripping over.

## Sketch

1. **Repeated request values.** `PageRequest.Query` and `Form` hold one value per name and a repeated
   field keeps its first, so a multi-select or `/search?q=a&q=b` loses everything but one value. Add
   `IReadOnlyList<string>` per name on the Sdk record, or a second dictionary beside the collapsed
   one, and keep `ctx.Request.Path` canonical. This is a page-authoring gap rather than a routing
   one: `LoginRedirect` already builds the return URL from the raw query string.
2. **No cookie a page may set.** `SimpleContext.SetHeader` refuses `Set-Cookie` along with every
   header `SecurityHeaders` owns, so a page cannot remember a preference between visits. The narrow
   version is `ctx.SetCookie(name, value)` bound to a page-owned name prefix, `HttpOnly`,
   `SameSite=Lax`, `Path=ctx.Path` and no expiry by default. The authentication, antiforgery and
   session cookies stay the shell's, and nothing may name them. If IDEA-014 lands, a preference is
   per-visitor state and this may not be needed at all.
3. **No encoding and no hashing.** The policy admits `System.Convert` but not `System.Text.Encoding`,
   so a page cannot turn a string into bytes; it admits nothing in `System.Security.Cryptography`, so
   a page cannot hash or sign. Both are already in the compiler's reference set — `PageReferenceSet`
   keeps every platform assembly that is not ours or `Microsoft.*` — so each is a policy entry and a
   test, not a reference change. Each also hands a capability to code the owner types into a text
   box, which is why they are separate lines rather than one "allow System" sweep. Bumping
   `PolicyVersion` rechecks every stored image (DEC-26), so ship them together or knowingly twice.
4. What stays out, and is not a gap: `System.Net.WebUtility` is unnecessary because `ctx.Write`
   already escapes; `System.Diagnostics` is how a page would time itself past the deadline;
   `System.Text.RegularExpressions` and `System.IO` are decisions in their own right, not oversights.

Changing `PageRequest`'s shape changes the SDK module version id, so every stored image is
recompiled on its next request (DEC-26). For a scriptable site that is the normal cost of a better
contract; it should still be one deliberate change and not a side effect of (1).
