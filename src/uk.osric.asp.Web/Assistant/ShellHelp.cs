// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;

/// <summary>One command of the virtual shell, as its <c>help</c> describes it.</summary>
/// <param name="Name">The command word, which is also the word <c>help</c> takes.</param>
/// <param name="Usage">The form it is called in, options and arguments included.</param>
/// <param name="Description">What it does, on one line, never wrapped.</param>
/// <param name="Notes">The syntax a caller cannot guess, when one line is not enough to say it.</param>
internal sealed record ShellHelpEntry(
    string Name,
    string Usage,
    string Description,
    string? Notes = null);

/// <summary>What the virtual shell's <c>help</c> command answers.</summary>
/// <remarks>
/// <see cref="VirtualShellParser"/> decides what is legal and this table is what the model is told,
/// so a test pins the two together in both directions the way <see cref="ShellRoutes"/> is pinned to
/// <see cref="ReservedPaths.Default"/>. The tool description is a line and the system prompt is not
/// a shell manual, so <c>help</c> is the whole of it: a model that guessed a POSIX form has no other
/// way to find this one. The preface says what the shell is not before the commands say what it is,
/// because every refused call in the production log was a host form that would have worked on a real
/// filesystem. One entry per line keeps an answer cheap and lets <c>grep</c> and a reader find a
/// command the same way.
/// </remarks>
internal static class ShellHelp {

    /// <summary>The preface: what the shell is not, before what it is.</summary>
    public const string Preface =
        "A read-only shell over the page store, which is a tree of keys: /notes is a page, and /vim\n"
        + "is a folder when a key lives beneath it. A key is a key, never a host path.\n"
        + "One command per line: there are no pipes, redirects, separators, globbing or expansion.\n"
        + "Quote a word with ' ' or \" \" to keep its spaces and punctuation literal, or escape one\n"
        + "character with a backslash: grep 'a|b' is a query, grep a|b is refused.";

    /// <summary>One entry per command, in ordinal order.</summary>
    public static IReadOnlyList<ShellHelpEntry> All { get; } = [
        new("cat", "cat [-n] <key> [key ...]",
            "Print each page's source, one after another. -n puts each line's number and a tab before it, counting each page from one.",
            "Several keys are read in the order they are written, and a number printed for one page can be\n"
            + "pasted into read_page or edit_page for that page."),
        new("find", "find [prefix]",
            "Print the long listing of everything below prefix, the same as ls -Rl."),
        new("grep", "grep [-i] [-n] [-r] [-l] [-c] <query> [prefix ...]",
            "Print key, line and text for every matching line at each prefix, or below it with -r.",
            "The query is a basic regular expression: ^ $ . * and [abc] are metacharacters, \\| is\n"
            + "either-this-or-that, and every other character is the literal one. -i ignores case, and -n\n"
            + "changes nothing because a match always carries its line. -l prints the keys that match,\n"
            + "one per key, and -c prints key:count for every key searched."),
        new("head", "head [<count>] <key>",
            "Print the first count lines of a page, ten by default.",
            "The count may be written plainly or after -n, as a shell spells it."),
        new("help", "help [command]", "Print this list, or the one command's usage and syntax."),
        new("ls", "ls [-l] [-R] [-r] [prefix]",
            "List the keys at prefix. -l puts the permission in front, -R lists everything below, and -r reverses the order."),
        new("stat", "stat <key> [key ...]",
            "Print each page's key, permission, size, revision hash, saved time and compile state."),
        new("tail", "tail [<count>] <key>",
            "Print the last count lines of a page, ten by default.",
            "The count may be written plainly or after -n, as a shell spells it."),
        new("wc", "wc [-l] <key>",
            "Print the number of lines in a page. -l is accepted and counts the same thing."),
    ];

    private static readonly Dictionary<string, ShellHelpEntry> s_byName =
        All.ToDictionary(entry => entry.Name, StringComparer.Ordinal);

    /// <summary>The entry for a command word, when the shell answers it.</summary>
    /// <param name="name">The word the model asked about.</param>
    /// <param name="entry">The entry, or null when the word is not a command.</param>
    public static bool TryGet(string name, out ShellHelpEntry? entry) => s_byName.TryGetValue(name, out entry);

    /// <summary>One entry's line, or every entry's, as the model receives it.</summary>
    /// <param name="entry">The command to describe, or null for the whole table.</param>
    /// <remarks>
    /// The list is one line per command so an answer stays cheap and a reader can find one; a single
    /// command also gets its notes, because that is the caller who has already chosen it.
    /// </remarks>
    public static string Text(ShellHelpEntry? entry = null) => entry is null
        ? Preface + "\n\n" + string.Join('\n', All.Select(Line))
        : entry.Notes is null ? Line(entry) : Line(entry) + "\n" + entry.Notes;

    private static string Line(ShellHelpEntry entry) => string.Concat(entry.Usage, ": ", entry.Description);
}
