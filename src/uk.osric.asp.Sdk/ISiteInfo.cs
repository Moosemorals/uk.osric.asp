// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>What the site knows about itself, as one fact sheet a page may read.</summary>
/// <remarks>
/// <para>
/// A page that draws one of the site's own views cannot read a file beside the assembly, so the host
/// normalises the facts once and offers them here. It is a closed set of typed members and never a
/// key lookup: there is no way to probe a credential, a connection string or the provider key.
/// </para>
/// <para>
/// Every member is a property and the type has no verbs. A page joins a URL with
/// <c>new Uri(info.Origin, "/path")</c> and scans the list it was given; it is the site's own code, so
/// it needs no guard against a URL from a model.
/// </para>
/// <para>
/// The documents, the changelist and the corpus are read once and held for the life of the process,
/// and the host warms them at startup. <see cref="Bounds"/> is the one member that moves: it is a
/// snapshot of what this render or job has left.
/// </para>
/// </remarks>
public interface ISiteInfo {

    /// <summary>The origin the site is reached at, from <c>Site:Origin</c>.</summary>
    public Uri Origin { get; }

    /// <summary>The canonical key of the site's editor page, from <c>Pages:Editor:Key</c>.</summary>
    public string EditorPath { get; }

    /// <summary>
    /// The commit this build was made from, or <see langword="null"/> when the build has none.
    /// </summary>
    /// <remarks>
    /// It is the build's own metadata, not the changelist's newest entry: a missing, stale or
    /// hand-edited changelist must not change what the site says it is running.
    /// </remarks>
    public string? DeployCommit { get; }

    /// <summary>Where the site's source lives, so a page can link a commit to its own page.</summary>
    public string RepositoryUrl { get; }

    /// <summary>The site's name, which identifies it to a remote service.</summary>
    public string Name { get; }

    /// <summary>The address a remote service can use to reach the owner.</summary>
    public string Contact { get; }

    /// <summary>
    /// The copyright and licence files the image carries, in a fixed order: the site's licence, the
    /// third-party notices, the licence texts by name, then the REUSE annotation.
    /// </summary>
    /// <remarks>Empty when the build carries none of them, never <see langword="null"/>.</remarks>
    public IReadOnlyList<SiteDocument> Documents { get; }

    /// <summary>The changelist the build carries, newest first.</summary>
    /// <remarks>
    /// Empty when the build carries no changelist, and empty when the file has no lines; a page cannot
    /// tell the two apart, because neither is a fact it can act on.
    /// </remarks>
    public IReadOnlyList<SiteCommit> Commits { get; }

    /// <summary>The documentation corpus this build carries, one per product assembly.</summary>
    /// <remarks>Empty when the build carries none, never <see langword="null"/>.</remarks>
    public IReadOnlyList<ReferenceCorpus> Reference { get; }

    /// <summary>What this render or job has left, read from the objects that enforce it.</summary>
    public SiteBounds Bounds { get; }
}
