// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>The permission column a long listing prints, answered in one place.</summary>
/// <remarks>
/// There is no host file mode here: <c>-rw-r--r--</c> means a save would be accepted and
/// <c>-r--r--r--</c> that it would not, and a folder carries the same answer with a leading
/// <c>d</c>. The assistant's tools and the virtual shell's <c>ls -l</c> and <c>stat</c> print the
/// same column, so they ask the same function.
/// </remarks>
public static class PageMode {

    /// <summary>The mode of a page or shell file.</summary>
    /// <param name="canWrite">Whether a save would be accepted.</param>
    public static string Describe(bool canWrite) => canWrite ? "-rw-r--r--" : "-r--r--r--";

    /// <summary>The mode of a folder.</summary>
    /// <param name="canWrite">Whether a save below it would be accepted.</param>
    public static string DescribeFolder(bool canWrite) => canWrite ? "drwxr-xr-x" : "dr-xr-xr-x";
}
