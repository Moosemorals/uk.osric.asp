<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# IDEA-011 — Pages cannot opt into authentication

| | |
| --- | --- |
| Size | Medium |
| Area | Web, Sdk, security, product |
| Location | `src/uk.osric.asp.Sdk/IContext.cs`, `src/uk.osric.asp.Web/Pages/PageEndpoint.cs`, `src/uk.osric.asp.Web/Authentication/` |

**Plan:** [Page authentication](../plans/page-authentication.md). The plan chooses the imperative
guard — `ctx.User` plus `ctx.RedirectToLogin()` — over the directive or metadata column this sketch
recommends, because the host cannot enforce a page's privacy without first running it and a flag adds
a parser/schema/editor/assistant surface for the same two statements. The rest of the sketch stands:
one local-account model, a validated local return target, CSRF on private-page `POST`s, and no roles
or per-user storage.

## Idea

[DEC-02](../decisions.md#page-contract-and-threat-model) makes every page public; only the editor
and the site's own routes require a signed-in local account. That is right for the shop window, but
it leaves no place for a page that is just for the owner: a draft, a personal dashboard, a page that
shows something private. The editor is not the place for those — they are pages with their own
content and their own reason to be signed in.

The smallest useful hook is per-page opt-in to the existing local-account sign-in. Keep the "one
actual user" model: there are local accounts, but no roles, claims, ownership or invitations. Any
signed-in local account is the same owner; a page either requires a signed-in visitor or does not.
The page can see who is signed in, so it can greet them and link to `/logout`.

This is not a full identity system, and it is not per-user storage. It is the hook that makes a
private page possible now and per-user state possible later.

## Sketch

1. **Opt-in shape.** Three candidates:

   - A template directive, for example `<%@ auth %>`, recognised by the tokenizer/generator and
     stored with the page. Author-visible, travels with the source, no editor UI. Needs a parser
     change and a rule that it appears once and at the top. (A marker inside `<%! %>` would have to
     be a C# comment or be stripped before C#, because `#auth` is not a valid preprocessor
     directive.)
   - A page metadata column, set by an editor checkbox ("Requires sign-in"). Explicit, queryable by
     the host before compiling or running the page, and available to the listing and the agent, but
     adds schema, editor UI and agent-tool surface.
   - A context call, `ctx.RequireAuthentication()`, at the top of the page. The smallest code
     change, but the page runs before the check, so an anonymous request executes page code and only
     then redirects; it is also easy to forget.

   Recommendation: **superseded by [the plan](../plans/page-authentication.md)**, which chooses the
   context call. The worry that the page runs before the check is smaller than it looks: the guard is
   the first author statement, and the body is buffered, so a redirect discards anything written
   before it. No parser, schema, editor control or agent-tool concept is needed.

2. **Enforcement.** For an anonymous request to a protected page:

   - Redirect to `/login?returnUrl=<path+query>` (`302`/`303`), so a browser gets the sign-in form
     and returns to the page. Reuse the existing `LoginEndpoint` return-target handling.
   - Or answer `404`, so a private page's existence is not revealed. This is a product decision.
     Recommendation: a login redirect for a signed-out owner, because the site has no audience to
     hide from; if drafts are expected, `404` is the alternative and should be a separate decision.

   A signed-in request runs normally. There is no `403` path in the one-user model.

3. **Identity in the contract.** Add a plain SDK value, for example `PageUser(string Name)` and
   `IContext.User`, populated from the authenticated principal. The page cannot forge it with a
   header or cookie; the shell maps the principal to the value. Keep it to `Name` for now; roles and
   claims are exactly what the one-user model avoids.

4. **CSRF is part of the hook.** The existing antiforgery middleware protects login, logout and
   editor POSTs, not pages. An auth-required page that accepts `POST` needs a token, or the sign-in
   cookie turns its form into a CSRF target. Options:

   - Expose the token or a ready-made hidden field through `IContext`, and require it on `POST`s to
     auth-required pages. Page forms include it; the host validates it.
   - Leave it to the page author with a documented helper. Smaller, but unsafe by default.

   Recommendation: the host validates the token and exposes the field through `IContext`, because
   "opt into authentication" should not quietly opt out of CSRF. Because the host does not know a
   page is private until it runs, [the plan](../plans/page-authentication.md) checks every `POST`
   whose principal is authenticated, not a page flag that does not exist.

5. **Return target.** The login redirect must preserve the query and `returnUrl` must be validated to
   a local target (the existing `LocalRedirect`/return-target code) so a protected page cannot become
   an open redirect. The fragment cannot be preserved: the browser never sends `#...` to the server.
   [The plan](../plans/page-authentication.md) records that correction and preserves the raw query
   rather than `ctx.Request.Query`, which collapses repeated names.

6. **Logout.** `/logout` already exists, but it is `POST`-only and validates antiforgery. The page
   posts a small form carrying `ctx.AntiforgeryField`; a bare link would fail. No new endpoint.

7. **Storage stays as it is.** [Page storage](../plans/archive/page-fetch-and-storage.md) is
   page-scoped and shared; authentication does not change that. Per-user keys are a later
   extension — `PagePath, UserName, Key`, or a separate table — and need their own migration and a
   decision about what happens to shared keys. Not this idea.

8. **Compilation and policy.** The opt-in is ordinary page code; it grants the page nothing it did
   not already have. `IContext.User` is an SDK type, so the page policy already allows it. No
   `System.Security` or ASP.NET types reach page code; only the plain value.

9. **Tests.** An anonymous `GET` to a protected page redirects with a validated `returnUrl`; a
   signed-in `GET` renders and sees the name; an unprotected page stays public; a protected `POST`
   without a token is refused and with a token succeeds; a page that is saved/recompiled keeps its
   requirement; `/logout` revokes access; a page cannot spoof the name with a header or cookie; an
   external `returnUrl` is refused.

10. **Documentation.** DEC-02 becomes "pages are public unless they opt in", with a decision for the
    one-user/no-roles model and for CSRF. The page contract, editor listing (if metadata is stored)
    and page-author prompt need the new state: a page may require sign-in, and the agent must write
    the requirement deliberately rather than assuming every page is public.

## Not this idea

- Roles, claims, groups, invitations, owner-per-page, a registration page or a second identity
  provider.
- Per-user storage. The auth hook makes it possible; it does not implement it.
- Hiding the site's own routes or changing the editor's `Editor` policy.
