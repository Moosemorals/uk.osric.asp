// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>How a request path marks a shell link as the place being shown.</summary>
/// <remarks>
/// It is a fact about the route, not a rendering rule: a page may mark its menu however it likes,
/// and using this keeps a page's menu agreeing with the shell's own.
/// </remarks>
public enum ShellMatch {

    /// <summary>The path is the link's own, and nothing beneath it.</summary>
    Leaf,

    /// <summary>The path is the link's own, or a page or route beneath it.</summary>
    Subtree,
}
