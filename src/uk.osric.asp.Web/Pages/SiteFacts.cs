// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Sdk;

/// <summary>The site's own facts, normalised once and held for the life of the process.</summary>
/// <remarks>
/// <para>
/// This is the one home for what a page may read about the site: the identity, the build's commit and
/// the three files the image carries. The routes that render those files read the same sources, so a
/// fact has one home and two callers.
/// </para>
/// <para>
/// Each source is a <see cref="Lazy{T}"/> over the reader the host supplied, so the startup warm-up
/// and the first page that asks share one read rather than racing. A page that asks for one facts
/// never waits for another's read.
/// </para>
/// </remarks>
internal sealed class SiteFacts {

    private readonly Lazy<IReadOnlyList<SiteDocument>> _documents;
    private readonly Lazy<IReadOnlyList<SiteCommit>> _commits;
    private readonly Lazy<IReadOnlyList<ReferenceCorpus>> _reference;
    private readonly SiteOptions _site;
    private readonly PageEditorOptions _editor;

    /// <summary>Creates the facts from the values the host read at startup.</summary>
    /// <param name="site">Where the host reaches the site it serves.</param>
    /// <param name="editor">The editor's key.</param>
    /// <param name="deployCommit">The build's commit, or <see langword="null"/> when it has none.</param>
    /// <param name="documents">Reads the files the image carries.</param>
    /// <param name="commits">Reads the changelist the build carries.</param>
    /// <param name="reference">Reads the documentation corpus the build carries.</param>
    public SiteFacts(
        SiteOptions site,
        PageEditorOptions editor,
        string? deployCommit,
        Func<IReadOnlyList<SiteDocument>> documents,
        Func<IReadOnlyList<SiteCommit>> commits,
        Func<IReadOnlyList<ReferenceCorpus>> reference) {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(reference);

        _site = site;
        _editor = editor;
        DeployCommit = string.IsNullOrWhiteSpace(deployCommit) ? null : deployCommit;
        _documents = new(documents, LazyThreadSafetyMode.ExecutionAndPublication);
        _commits = new(commits, LazyThreadSafetyMode.ExecutionAndPublication);
        _reference = new(reference, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The origin the site is reached at.</summary>
    public Uri Origin => _site.Origin;

    /// <summary>The editor's canonical key.</summary>
    public string EditorPath => _editor.Key;

    /// <summary>The build's commit, or <see langword="null"/> when it has none.</summary>
    public string? DeployCommit { get; }

    /// <summary>Where the site's source lives, which is the same for every render.</summary>
    public static string RepositoryUrl => SiteIdentity.RepositoryUrl;

    /// <summary>The site's name, which is the same for every render.</summary>
    public static string Name => SiteIdentity.Name;

    /// <summary>The owner's address, which is the same for every render.</summary>
    public static string Contact => SiteIdentity.Contact;

    /// <summary>The copyright and licence files the image carries.</summary>
    public IReadOnlyList<SiteDocument> Documents => _documents.Value;

    /// <summary>The changelist the build carries, newest first.</summary>
    public IReadOnlyList<SiteCommit> Commits => _commits.Value;

    /// <summary>The documentation corpus the build carries.</summary>
    public IReadOnlyList<ReferenceCorpus> Reference => _reference.Value;

    /// <summary>Reads every source on the thread pool, so the first page that asks finds it.</summary>
    /// <param name="cancellationToken">Cancels a read that has not started.</param>
    /// <returns>A task that completes when all three reads have finished.</returns>
    /// <remarks>
    /// The three reads run apart, so a slow one does not hold up a caller that asks for another. A
    /// source that cannot be read answers a value rather than throwing, so a warm-up can never leave a
    /// <see cref="Lazy{T}"/> holding a failure.
    /// </remarks>
    public Task WarmAsync(CancellationToken cancellationToken = default) {
        return Task.WhenAll(
            Task.Run(() => _ = Documents, cancellationToken),
            Task.Run(() => _ = Commits, cancellationToken),
            Task.Run(() => _ = Reference, cancellationToken));
    }
}
