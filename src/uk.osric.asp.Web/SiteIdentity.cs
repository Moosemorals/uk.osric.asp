// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web;

/// <summary>The site's identity, in the one place its own code and its pages both read it.</summary>
/// <remarks>
/// These stay constants because the site's identity has not changed: development runs on loopback
/// while the name, the contact address and the repository are the same. Configuration is what an
/// installation can differ on, and these are what the site is.
/// </remarks>
internal static class SiteIdentity {

    /// <summary>The site's name, which identifies it to a remote service.</summary>
    public const string Name = "moosemorals.com";

    /// <summary>The owner's address, which a remote service can use to make contact.</summary>
    public const string Contact = "fetchmoose@fluffypeople.com";

    /// <summary>Where the site's source lives, which a commit hash links to.</summary>
    public const string RepositoryUrl = "https://github.com/Moosemorals/uk.osric.asp";
}
