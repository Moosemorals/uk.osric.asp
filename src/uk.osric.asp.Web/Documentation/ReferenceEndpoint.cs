// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Globalization;
using System.Net;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Errors;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Http;
using uk.osric.asp.Web.Pages;

/// <summary>The pages that read the documentation this build carries.</summary>
/// <remarks>
/// The site owns these routes because the corpus is a build artifact the image carries, like the
/// changelist: a page cannot read a file beside the assembly. They are public even though the key is
/// under <c>/admin</c> — the URL is a filing cabinet, not an authorisation — and they render the
/// admin menu because that is where a reader finds them.
/// </remarks>
internal static class ReferenceEndpoint {

    /// <summary>Answers <c>GET /admin/reference</c>: this build's assemblies.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="library">The corpus this build carries.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public static async Task IndexAsync(
        HttpContext context,
        ReferenceLibrary library,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(library);

        await ShellDocument.BeginAsync(
            context,
            "Reference",
            SiteNavigation.For(context),
            stylesheets: ["/reference.css"],
            cancellationToken: cancellationToken);

        await context.Response.WriteAsync("<h1>Reference</h1>", cancellationToken);
        await context.Response.WriteAsync(AdminMenu(context), cancellationToken);
        await context.Response.WriteAsync(
            "<p>The documentation this build's assemblies carry, generated from their source comments. "
            + "It describes the code serving this request, not a version on GitHub.</p>",
            cancellationToken);

        if (BuildInfo.Current.Commit is { } commit) {
            string shortCommit = commit[..Math.Min(7, commit.Length)];
            await context.Response.WriteAsync(
                $"<p class=\"reference-build\">Built from <a href=\"{SiteIdentity.RepositoryUrl}/commit/{WebUtility.HtmlEncode(commit)}\"><code>{WebUtility.HtmlEncode(shortCommit)}</code></a>.</p>",
                cancellationToken);
        }

        await context.Response.WriteAsync("<h2>Assemblies</h2>", cancellationToken);

        if (library.Assemblies.Count == 0) {
            await context.Response.WriteAsync(
                "<p>This build carries no documentation. <code>GenerateDocumentationFile</code> writes it and "
                + "the publish copies it beside each assembly, so a build that did not come through the "
                + "Makefile has none.</p>",
                cancellationToken);
        } else {
            await context.Response.WriteAsync("<ul class=\"reference-assemblies\">", cancellationToken);
            foreach (ReferenceAssembly assembly in library.Assemblies) {
                ReferenceCorpus corpus = assembly.Corpus;
                await context.Response.WriteAsync(
                    $"<li><a href=\"/admin/reference/{WebUtility.HtmlEncode(corpus.Slug)}\">{WebUtility.HtmlEncode(corpus.Title)}</a> "
                    + $"<span class=\"reference-count\">{Count(corpus.TypeCount, "type", "types")}, {Count(corpus.EntryCount, "entry", "entries")}, "
                    + $"{Count(corpus.Namespaces.Count, "namespace", "namespaces")}</span><br>"
                    + $"<code>{WebUtility.HtmlEncode(corpus.Assembly)}</code> — {WebUtility.HtmlEncode(corpus.Description)}</li>",
                    cancellationToken);
            }

            await context.Response.WriteAsync("</ul>", cancellationToken);
        }

        await context.Response.WriteAsync("""
            <h2>What this page shows</h2>
            <p>Every entry is the documentation the compiler emitted for this build, so it is not a filtered
            public surface: a documented <code>internal</code> type is here too. A declaration says its
            accessibility, its return type and whether it is a class, a record, an interface or an enum.</p>
            <p>A member that inherits its documentation carries the source's words and the line that says
            where they came from. Where the source is outside this build — the BCL, EF Core — the entry names
            it and links to its page on Microsoft Learn when the URL can be computed.</p>
            """, cancellationToken);

        await ShellDocument.EndAsync(context, cancellationToken);
    }

    /// <summary>Answers <c>GET /admin/reference/{slug}</c>: one assembly.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="slug">The assembly's URL segment.</param>
    /// <param name="library">The corpus this build carries.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public static async Task AssemblyAsync(
        HttpContext context,
        string slug,
        ReferenceLibrary library,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(library);

        if (library.Find(slug) is not { } assembly) {
            await NotFoundAsync(context, $"There is no documentation for \u201c{slug}\u201d in this build.", cancellationToken);
            return;
        }

        if (assembly.Corpus.Namespaces.Count == 1) {
            await NamespacePageAsync(context, assembly, assembly.Corpus.Namespaces[0], cancellationToken);
            return;
        }

        await NamespaceIndexAsync(context, assembly, cancellationToken);
    }

    /// <summary>Answers <c>GET /admin/reference/{slug}/{namespace}</c>: one namespace.</summary>
    /// <param name="context">The response being written.</param>
    /// <param name="slug">The assembly's URL segment.</param>
    /// <param name="namespace">The namespace's name.</param>
    /// <param name="library">The corpus this build carries.</param>
    /// <param name="cancellationToken">Aborted when the request is abandoned.</param>
    public static async Task NamespaceAsync(
        HttpContext context,
        string slug,
        string @namespace,
        ReferenceLibrary library,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(library);

        if (library.Find(slug) is not { } assembly) {
            await NotFoundAsync(context, $"There is no documentation for \u201c{slug}\u201d in this build.", cancellationToken);
            return;
        }

        ReferenceNamespace? space = assembly.Corpus.Namespaces
            .FirstOrDefault(candidate => string.Equals(candidate.Name, @namespace, StringComparison.Ordinal));
        if (space is null) {
            await NotFoundAsync(context, $"There is no namespace called \u201c{@namespace}\u201d in {assembly.Info.AssemblyName}.", cancellationToken);
            return;
        }

        await NamespacePageAsync(context, assembly, space, cancellationToken);
    }

    private static async Task NamespacePageAsync(
        HttpContext context,
        ReferenceAssembly assembly,
        ReferenceNamespace space,
        CancellationToken cancellationToken) {
        ReferenceCorpus corpus = assembly.Corpus;
        string title = corpus.Namespaces.Count == 1 ? corpus.Title : corpus.Title + " — " + space.Name;
        string script = await ShellDocument.AssetUrlAsync(context, "/reference.js", cancellationToken);

        // The type index opens a <details> when a link names one, so this page is the one shell view
        // that loads a script; the default policy forbids every script.
        SecurityHeaders.AllowScripts(context);

        await ShellDocument.BeginAsync(
            context,
            title,
            SiteNavigation.For(context),
            stylesheets: ["/reference.css"],
            head: $"<script type=\"module\" src=\"{WebUtility.HtmlEncode(script)}\"></script>",
            cancellationToken: cancellationToken);

        await context.Response.WriteAsync($"<h1>{WebUtility.HtmlEncode(corpus.Title)}</h1>", cancellationToken);
        await context.Response.WriteAsync(AdminMenu(context), cancellationToken);
        await context.Response.WriteAsync(
            $"<p class=\"reference-meta\"><a href=\"/admin/reference\">All assemblies</a> · "
            + $"<code>{WebUtility.HtmlEncode(corpus.Assembly)}</code> · "
            + $"{Count(corpus.TypeCount, "type", "types")} · {Count(corpus.EntryCount, "entry", "entries")}</p>",
            cancellationToken);

        if (corpus.Namespaces.Count > 1) {
            await context.Response.WriteAsync(
                NamespaceNavigation(corpus, space.Name),
                cancellationToken);
        }

        await context.Response.WriteAsync(
            $"<h2 class=\"namespace-heading\"><code>{WebUtility.HtmlEncode(DisplayName(space.Name))}</code> "
            + $"<span class=\"reference-count\">{Count(space.TypeCount, "type", "types")}</span></h2>",
            cancellationToken);
        await context.Response.WriteAsync(assembly.Render(space), cancellationToken);

        await ShellDocument.EndAsync(context, cancellationToken);
    }

    private static async Task NamespaceIndexAsync(
        HttpContext context,
        ReferenceAssembly assembly,
        CancellationToken cancellationToken) {
        ReferenceCorpus corpus = assembly.Corpus;

        await ShellDocument.BeginAsync(
            context,
            corpus.Title,
            SiteNavigation.For(context),
            stylesheets: ["/reference.css"],
            cancellationToken: cancellationToken);

        await context.Response.WriteAsync($"<h1>{WebUtility.HtmlEncode(corpus.Title)}</h1>", cancellationToken);
        await context.Response.WriteAsync(AdminMenu(context), cancellationToken);
        await context.Response.WriteAsync(
            $"<p class=\"reference-meta\"><a href=\"/admin/reference\">All assemblies</a> · "
            + $"<code>{WebUtility.HtmlEncode(corpus.Assembly)}</code> · "
            + $"{Count(corpus.TypeCount, "type", "types")} · {Count(corpus.EntryCount, "entry", "entries")}</p>"
            + $"<p>{WebUtility.HtmlEncode(corpus.Description)}</p>",
            cancellationToken);

        await context.Response.WriteAsync(
            "<h2>Namespaces</h2><ul class=\"namespace-list\">",
            cancellationToken);
        foreach (ReferenceNamespace space in corpus.Namespaces) {
            await context.Response.WriteAsync(
                $"<li><a href=\"/admin/reference/{WebUtility.HtmlEncode(corpus.Slug)}/{WebUtility.HtmlEncode(space.Name)}\">"
                + $"<code>{WebUtility.HtmlEncode(DisplayName(space.Name))}</code></a> "
                + $"<span class=\"reference-count\">{Count(space.TypeCount, "type", "types")}</span></li>",
                cancellationToken);
        }

        await context.Response.WriteAsync("</ul>", cancellationToken);
        await ShellDocument.EndAsync(context, cancellationToken);
    }

    private static string NamespaceNavigation(ReferenceCorpus corpus, string current) {
        List<NavLink> links = [.. corpus.Namespaces.Select(space => new NavLink(
            "/admin/reference/" + corpus.Slug + "/" + Uri.EscapeDataString(space.Name),
            DisplayName(space.Name),
            string.Equals(space.Name, current, StringComparison.Ordinal)))];
        return ShellDocument.Nav("Namespaces in this assembly", links, "namespace-nav");
    }

    private static string DisplayName(string name) => name.Length == 0 ? "(global)" : name;

    private static async Task NotFoundAsync(
        HttpContext context,
        string explanation,
        CancellationToken cancellationToken) {
        bool isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        await ErrorResponses.WriteAsync(
            context,
            StatusCodes.Status404NotFound,
            new ErrorMessage(
                "reference.not_found",
                "Not found",
                explanation + " The reference lists the assemblies it carries.",
                [
                    new NavLink("/admin/reference", "All assemblies"),
                    .. ErrorMessage.DefaultRemedies(isAuthenticated, EditorPaths.Of(context.RequestServices)),
                ]),
            cancellationToken);
    }

    private static string AdminMenu(HttpContext context) =>
        AdminNavigation.Nav(context);

    // "entry" does not pluralise with an "s", so the caller names both forms.
    private static string Count(int count, string singular, string plural) =>
        count.ToString("N0", CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);
}
