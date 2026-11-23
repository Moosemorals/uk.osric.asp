// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using uk.osric.asp.Sdk;

/// <summary>Why the changelist is what it is.</summary>
internal enum ChangeListReason {
    /// <summary>The file was read; it may still hold no lines.</summary>
    Ok,

    /// <summary>The build carries no changelist.</summary>
    Missing,

    /// <summary>The file is there but could not be read.</summary>
    Unreadable,
}

/// <summary>The changelist and the reason it is what it is.</summary>
/// <param name="Commits">The parsed lines, newest first, empty when there are none.</param>
/// <param name="Reason">Whether the file was read, missing or unreadable.</param>
internal sealed record ChangeListRead(IReadOnlyList<SiteCommit> Commits, ChangeListReason Reason);

/// <summary>The changelist: every commit in the deployed build, newest first.</summary>
/// <remarks>
/// <para>
/// The list is a file written by <c>make changelist</c> and packed beside the assembly, because the
/// running image has no source tree and no git. Each line is a full hash and a subject, which is what
/// lets the hash link to the commit's own page instead of being decoration.
/// </para>
/// <para>
/// The page sees the list and nothing else; the route sees the reason, because a reader is owed the
/// difference between a build without a changelist and one whose file could not be read. The file is
/// read once and held.
/// </para>
/// </remarks>
internal sealed class ChangeList {

    private readonly AdminOptions _options;
    private readonly ILogger<ChangeList> _logger;
    private readonly Lazy<ChangeListRead> _read;

    /// <summary>Reads the file the admin options name, on first use.</summary>
    /// <param name="options">Names the file.</param>
    /// <param name="logger">Where an unreadable file is reported.</param>
    public ChangeList(AdminOptions options, ILogger<ChangeList> logger) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
        _read = new Lazy<ChangeListRead>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The changelist and why it is what it is.</summary>
    public ChangeListRead Read => _read.Value;

    /// <summary>The file's path; a relative setting is resolved beside the assembly.</summary>
    /// <param name="options">Names the file.</param>
    public static string PathOf(AdminOptions options) {
        ArgumentNullException.ThrowIfNull(options);

        return Path.IsPathRooted(options.Changelist)
            ? options.Changelist
            : Path.Combine(AppContext.BaseDirectory, options.Changelist);
    }

    /// <summary>The file's lines, with a full hash recognised as a commit.</summary>
    /// <param name="text">The file's contents.</param>
    /// <returns>One entry per non-empty line, in the order the file holds them.</returns>
    public static IReadOnlyList<SiteCommit> Parse(string text) {
        ArgumentNullException.ThrowIfNull(text);

        List<SiteCommit> commits = [];

        foreach (string line in text.Split('\n')) {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) {
                continue;
            }

            int space = trimmed.IndexOf(' ');
            string first = space < 0 ? trimmed : trimmed[..space];
            commits.Add(IsCommitHash(first)
                ? new SiteCommit(first, space < 0 ? string.Empty : trimmed[(space + 1)..])
                : new SiteCommit(null, trimmed));
        }

        return commits;
    }

    private static bool IsCommitHash(string value) =>
        value.Length == 40 && value.All(char.IsAsciiHexDigit);

    private ChangeListRead Load() {
        string path = PathOf(_options);

        // A path that names a folder is a setting that cannot be read, not a build without a
        // changelist: an operator needs to see the difference in the page.
        if (Directory.Exists(path)) {
            _logger.LogWarning("The changelist setting names a folder at {Path}; the page will say so", path);

            return new ChangeListRead([], ChangeListReason.Unreadable);
        }

        if (!File.Exists(path)) {
            return new ChangeListRead([], ChangeListReason.Missing);
        }

        try {
            return new ChangeListRead(Parse(File.ReadAllText(path)), ChangeListReason.Ok);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            _logger.LogWarning(
                exception,
                "Could not read the changelist at {Path}; the page will say so",
                path);

            return new ChangeListRead([], ChangeListReason.Unreadable);
        }
    }
}
