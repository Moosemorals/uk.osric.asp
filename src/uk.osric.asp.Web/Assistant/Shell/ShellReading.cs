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
    public static async ValueTask<(List<ShellFile> Files, ShellResult? Refusal)> PagesAsync(
        IReadOnlyList<ShellPath> paths,
        IPageStoreView store,
        string command,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(command);

        List<ShellFile> files = [];
        foreach (ShellPath path in paths) {
            ShellFile? file = await store.ReadAsync(path, cancellationToken);
            if (file is null) {
                return ([], ShellResult.Refused(
                    $"{path.Key.Key} is a folder, not a page. ls {path.Key.Key} lists what is in it.",
                    $"{command} {path.Key.Key}: a folder"));
            }

            files.Add(file);
        }

        return (files, null);
    }
}
