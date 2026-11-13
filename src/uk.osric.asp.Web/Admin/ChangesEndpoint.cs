// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net;
using uk.osric.asp.Web.Html;
using uk.osric.asp.Web.Pages;

/// <summary>The changelist: every commit in the deployed build, newest first.</summary>
/// <remarks>
/// The list is a file written by <c>make changelist</c> and packed beside the assembly, because the
/// running image has no source tree and no git. Each line is a full hash and a subject, which is what
/// lets the hash link to the commit's own page on GitHub instead of being decoration.
/// </remarks>
internal static class ChangesEndpoint {

    /// <summary>Where a commit's own page lives.</summary>
    internal const string RepositoryUrl = "https://github.com/Moosemorals/uk.osric.asp";

    /// <summary>The file the build packs, unless configuration names another.</summary>
    private const string DefaultFile = "changes.txt";

    /// <summary>Renders the changelist.</summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="configuration">Names the file through <c>Admin:Changelist</c>.</param>
    public static async Task HandleAsync(HttpContext context, IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configuration);

        string? text = await ReadAsync(configuration, context.RequestAborted);
        IReadOnlyList<Commit> commits = text is null ? [] : Parse(text);

        await ShellDocument.BeginAsync(context, "Changelist", SiteNavigation.For(context), stylesheets: ["/admin.css"]);
        await context.Response.WriteAsync("<h1>Changelist</h1>");
        await context.Response.WriteAsync(AdminNavigation.Nav("changes", EditorPaths.Of(context.RequestServices)));

        if (text is null) {
            await context.Response.WriteAsync("""
                <p>This build carries no changelist. <code>make changelist</code> writes it and the
                build packs it, so a build that did not come through the Makefile has none.</p>
                """);
        } else {
            await context.Response.WriteAsync($"<p>{commits.Count.ToString(CultureInfo.InvariantCulture)} commits, newest first. Each links to its page on GitHub.</p>");
            await context.Response.WriteAsync("<ul class=\"changelist\">");

            foreach (Commit commit in commits) {
                string subject = WebUtility.HtmlEncode(commit.Subject);
                await context.Response.WriteAsync(commit.Hash is { } hash
                    ? $"""<li><a href="{RepositoryUrl}/commit/{hash}"><code>{Short(hash)}</code> {subject}</a></li>"""
                    : $"<li>{subject}</li>");
            }

            await context.Response.WriteAsync("</ul>");
        }

        await ShellDocument.EndAsync(context);
    }

    /// <summary>The file's contents, or <see langword="null"/> when this build carries none.</summary>
    internal static async Task<string?> ReadAsync(IConfiguration configuration, CancellationToken cancellationToken) {
        string path = PathOf(configuration);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    /// <summary>The file's path; a relative setting is resolved beside the assembly.</summary>
    internal static string PathOf(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        string configured = configuration["Admin:Changelist"] is { Length: > 0 } value ? value : DefaultFile;
        return Path.IsPathRooted(configured) ? configured : Path.Combine(AppContext.BaseDirectory, configured);
    }

    /// <summary>The file's lines, with a full hash recognised as a commit.</summary>
    internal static IReadOnlyList<Commit> Parse(string text) {
        ArgumentNullException.ThrowIfNull(text);

        List<Commit> commits = [];

        foreach (string line in text.Split('\n')) {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) {
                continue;
            }

            int space = trimmed.IndexOf(' ');
            string first = space < 0 ? trimmed : trimmed[..space];
            commits.Add(IsCommitHash(first)
                ? new Commit(first, space < 0 ? string.Empty : trimmed[(space + 1)..])
                : new Commit(null, trimmed));
        }

        return commits;
    }

    private static bool IsCommitHash(string value) =>
        value.Length == 40 && value.All(char.IsAsciiHexDigit);

    private static string Short(string hash) => hash[..7];

    /// <summary>One line: a full hash and its subject, or text that is not a commit.</summary>
    internal sealed record Commit(string? Hash, string Subject);
}
