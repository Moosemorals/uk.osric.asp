// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The build that is answering the request. Deprecated: use <see cref="ISiteInfo"/>.</summary>
/// <remarks>
/// <para>
/// This was a capability of its own because a page may not read the assembly or the environment; it
/// is one of the site's facts now, so <see cref="ISiteInfo.DeployCommit"/> answers the same value and
/// every render offers <see cref="ISiteInfo"/>. The interface and its one member are kept for one
/// release so an unedited page still compiles and runs.
/// </para>
/// <para>
/// A build made outside a checkout records no commit, so the value is <see langword="null"/> rather
/// than a placeholder a page might mistake for a hash.
/// </para>
/// </remarks>
[Obsolete(SiteInfoDeprecation.BuildInfo, DiagnosticId = SiteInfoDeprecation.DiagnosticId)]
public interface IBuildInfo {

    /// <summary>The full git commit hash, or <see langword="null"/> when the build has none.</summary>
    public string? Commit { get; }
}
