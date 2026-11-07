// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The local account a page is running for, as a plain value.</summary>
/// <remarks>
/// Deliberately smaller than the host's own account record: a page learns who is signed in, not the
/// password hash, the security stamp or the row. Only <see cref="IContext.User"/> creates one, so a
/// page cannot construct an identity the shell will believe.
/// </remarks>
/// <param name="Name">The normalised user name from the sign-in cookie.</param>
public sealed record PageUser(string Name);
