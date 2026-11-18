// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Who a shell link is for.</summary>
/// <remarks>
/// A page filters on this with its own <see cref="IContext.User"/>, so a menu a page draws agrees
/// with the guard on the page it links to.
/// </remarks>
public enum ShellAudience {

    /// <summary>Every visitor, signed in or not.</summary>
    Everyone,

    /// <summary>A visitor who has not signed in.</summary>
    Anonymous,

    /// <summary>A signed-in visitor, which on this site is the owner.</summary>
    Owner,
}
