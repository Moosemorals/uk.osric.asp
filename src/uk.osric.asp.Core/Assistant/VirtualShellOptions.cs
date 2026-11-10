// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

/// <summary>The options a virtual-shell command was given.</summary>
/// <remarks>
/// One flag set rather than a property for each option: the parser answers what the command line said
/// and the executor decides what its command does with it, so an option that only one command takes
/// is still one member here and nothing else about the value changes.
/// </remarks>
[Flags]
public enum VirtualShellOptions {

    /// <summary>The command was given no option.</summary>
    None = 0,

    /// <summary><c>-n</c>: number the lines the command prints.</summary>
    Numbers = 1,

    /// <summary><c>-i</c>: match without regard to case.</summary>
    IgnoreCase = 2,

    /// <summary><c>-l</c>: the long listing, with a permission column in front of every key.</summary>
    Long = 4,

    /// <summary><c>-r</c> or <c>-R</c>: read everything below the path, not only what is at it.</summary>
    Recursive = 8,

    /// <summary><c>-r</c> on <c>ls</c>: the listing in reverse order, as a shell's <c>ls</c> means it.</summary>
    Reverse = 16,

    /// <summary><c>-l</c> on <c>grep</c>: the keys that match, instead of the lines that do.</summary>
    Names = 32,

    /// <summary><c>-c</c> on <c>grep</c>: how many lines match in each key, instead of the lines.</summary>
    Counts = 64,
}
