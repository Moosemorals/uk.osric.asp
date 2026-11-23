// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The messages the doors <see cref="ISiteInfo"/> replaces carry for one release.</summary>
/// <remarks>
/// Internal, so they are not part of the API a page binds against, and one constant each so an
/// unedited page's warning cannot drift from what replaces it.
/// </remarks>
internal static class SiteInfoDeprecation {

    /// <summary>The diagnostic id an old door reports, which is never reused.</summary>
    internal const string DiagnosticId = "PAGE013";

    /// <summary>What a page should say instead of <c>IBuildInfo</c>.</summary>
    internal const string BuildInfo =
        "Use ISiteInfo.DeployCommit: every render offers ISiteInfo, which carries the build's commit and the site's other facts.";

    /// <summary>What a page should say instead of <c>ctx.EditorPath</c>.</summary>
    internal const string EditorPath =
        "Use ctx.Capabilities.GetRequired<ISiteInfo>().EditorPath: the editor's key is one of the site's facts now.";
}
