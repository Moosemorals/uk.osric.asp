// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>One layer's or one job's view of the site's facts.</summary>
/// <remarks>
/// The process-wide values come from <see cref="SiteFacts"/>; <see cref="Bounds"/> is asked afresh
/// each time it is read, because it is a snapshot of what the render or job has left.
/// </remarks>
internal sealed class SiteInfo : ISiteInfo {

    private readonly SiteFacts _facts;
    private readonly Func<SiteBounds> _bounds;

    /// <summary>Creates the view for one layer or one job.</summary>
    /// <param name="facts">The process-wide facts.</param>
    /// <param name="bounds">Builds the snapshot of what the caller has left.</param>
    public SiteInfo(SiteFacts facts, Func<SiteBounds> bounds) {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(bounds);

        _facts = facts;
        _bounds = bounds;
    }

    /// <inheritdoc/>
    public Uri Origin => _facts.Origin;

    /// <inheritdoc/>
    public string EditorPath => _facts.EditorPath;

    /// <inheritdoc/>
    public string? DeployCommit => _facts.DeployCommit;

    /// <inheritdoc/>
    public string RepositoryUrl => SiteFacts.RepositoryUrl;

    /// <inheritdoc/>
    public string Name => SiteFacts.Name;

    /// <inheritdoc/>
    public string Contact => SiteFacts.Contact;

    /// <inheritdoc/>
    public IReadOnlyList<SiteDocument> Documents => _facts.Documents;

    /// <inheritdoc/>
    public IReadOnlyList<SiteCommit> Commits => _facts.Commits;

    /// <inheritdoc/>
    public IReadOnlyList<ReferenceCorpus> Reference => _facts.Reference;

    /// <inheritdoc/>
    public SiteBounds Bounds => _bounds();
}
