// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Shell;

/// <summary>Reads the pages a command's operands named, or the refusal that says one is a folder.</summary>
/// <remarks>
/// Every command that reads pages refuses a folder the same way, naming the command in the result's
/// summary, so the wording lives here rather than in each applet.
/// </remarks>
internal static class ShellReading {

    /// <summary>The files the paths name, in the order they were written, or the first refusal.</summary>
    /// <param name="paths">The operands, already expanded and validated.</param>
    /// <param name="store">The store to read them from.</param>
    /// <param name="command">The command's word, for the refusal's summary.</param>
    /// <param name="cancellationToken">The run's token.</param>
    public static async ValueTask<(List<PageRead> Files, ShellResult? Refusal)> PagesAsync(
        IReadOnlyList<PageNode> paths,
        IPageStoreReader store,
        string command,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(command);

        List<PageRead> files = [];
        foreach (PageNode path in paths) {
            PageRead? file = await store.ReadAsync(path.Path, cancellationToken);
            if (file is null) {
                return ([], ShellResult.Refused(
                    $"{path.Path.Key} is a folder, not a page. ls {path.Path.Key} lists what is in it.",
                    $"{command} {path.Path.Key}: a folder"));
            }

            files.Add(file);
        }

        return (files, null);
    }
}
