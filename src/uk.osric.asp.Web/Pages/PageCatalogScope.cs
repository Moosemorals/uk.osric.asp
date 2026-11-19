// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>One layer's <see cref="IPageCatalog"/>, bound to that render.</summary>
/// <remarks>
/// <para>
/// It is offered to every layer, so a layout may read the store as well as a page may. There is no
/// gate: the read answers for whoever the calling page answers for, and a page that wants its
/// listing or its source private writes the ordinary <c>ctx.User</c> guard in its own source.
/// </para>
/// <para>
/// The shell links are the list the endpoint built once for this request, so every layer of one
/// render sees one answer and the sign-in link carries the visitor's way back. A worker's list is
/// empty: it has no request and no visitor.
/// </para>
/// <para>
/// The only refusals are about the call itself. An argument that cannot name anything is an
/// <see cref="ArgumentException"/>, because that is a page bug; a limit past the cap is an
/// <see cref="ArgumentOutOfRangeException"/> that names the setting, rather than a silently trimmed
/// answer; and an abandoned render is an <see cref="InvalidOperationException"/>. A store with more
/// rows than the slice asked for is not a refusal: the answer says so.
/// </para>
/// </remarks>
internal sealed class PageCatalogScope(
    PagePath path,
    IReadOnlyList<ShellLink> shellLinks,
    IPageReadStore store,
    PageReadOptions options,
    ReadBudget budget,
    ReservedPaths reserved,
    PageContentTypes contentTypes,
    PageSeeds seeds,
    string currentRuntimeId) : RenderBoundCapability, IPageCatalog {

    /// <inheritdoc/>
    public IReadOnlyList<ShellLink> ShellLinks() {
        ThrowIfEnded();

        return shellLinks;
    }

    /// <inheritdoc/>
    public async Task<PageListing> ListAsync(PageListingRequest request) {
        ArgumentNullException.ThrowIfNull(request);

        string key = NormalizePrefix(request.Prefix);
        string? after = request.After is null ? null : NormalizeKey(request.After);
        int limit = request.Limit ?? options.MaxRowsPerList;

        if (limit < 1 || limit > options.MaxRowsPerList) {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                limit,
                $"A listing of {path.Key} asked for {limit} rows; a limit is 1 to {options.MaxRowsPerList}. "
                + "Raise Pages:Read:MaxRowsPerList, or ask for a slice and resume from the last key.");
        }

        ThrowIfEnded();

        // One row past the limit is how the store says "there are more" without a second query.
        IReadOnlyList<PageReadHead> rows = await store.ListAsync(key, after, limit + 1, Cancellation());
        bool truncated = rows.Count > limit;

        List<PageEntry> entries = [];
        int shown = Math.Min(rows.Count, limit);
        entries.EnsureCapacity(shown);
        for (int index = 0; index < shown; index++) {
            PageReadHead row = rows[index];
            PageImageState state = PageImageStatus.Describe(
                row.RuntimeId, row.HasImage, row.HasDiagnostics, currentRuntimeId, row.TemplateKind);
            entries.Add(new PageEntry(
                row.Key,
                row.SavedAt,
                row.Hash,
                row.IsDeleted,
                reserved.IsReserved(PagePath.Normalize(row.Key)),
                IsHtml(row.Key)) {
                IsAsset = row.TemplateKind.IsAsset(),
                IsLibrary = row.TemplateKind.IsLibrary(),
                IsWorker = row.TemplateKind.IsWorker(),
                State = state.Describe(),
                Libraries = PageLibraryFingerprint.KeysOf(row.TemplateLibraries),
                Seed = Describe(row),
            });
        }

        return new PageListing(entries, truncated);
    }

    /// <inheritdoc/>
    public async Task<PageSource?> ReadAsync(string key) {
        string normalized = NormalizeKey(key);
        ThrowIfEnded();

        PageReadSource? source = await store.GetSourceAsync(normalized, Cancellation());
        if (source is null) {
            return null;
        }

        Charge(source.Template.Length, source.Key);

        return new PageSource(source.Key, source.Hash, source.SavedAt, source.Template);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageRevisionInfo>> RevisionsAsync(string key) {
        string normalized = NormalizeKey(key);
        ThrowIfEnded();

        IReadOnlyList<PageReadRevision> revisions = await store.ListRevisionsAsync(normalized, Cancellation());

        return [.. revisions.Select(revision =>
            new PageRevisionInfo(revision.Hash, revision.SavedAt, revision.IsCurrent))];
    }

    /// <inheritdoc/>
    public async Task<PageSource?> ReadRevisionAsync(string key, string hash) {
        string normalized = NormalizeKey(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ThrowIfEnded();

        PageReadSource? source = await store.GetRevisionSourceAsync(normalized, hash, Cancellation());
        if (source is null) {
            return null;
        }

        Charge(source.Template.Length, $"{source.Key} at {source.Hash}");

        return new PageSource(source.Key, source.Hash, source.SavedAt, source.Template);
    }

    // The row the store answered for, judged against the pack the process read once. A key with no
    // seed on either side is the owner's own page, and carries no summary.
    private PageSeedSummary? Describe(PageReadHead row) {
        PagePath path = PagePath.Normalize(row.Key);
        SeedFile? seed = seeds.Find(path);

        if (row.IsDeleted) {
            return seed is null ? null : SeedVerdictWords.Deleted();
        }

        if (seed is null) {
            return row.SeedHash is null
                ? null
                : SeedComparison.Of(row.SeedHash, null, row.ContentHash).Describe(null);
        }

        return SeedComparison.Of(row.SeedHash, seed.Hash, row.ContentHash).Describe(seed.Hash);
    }

    // "" and "/" is the whole store; anything else is a key, so case, a trailing slash and a dot
    // segment all normalise the way every other page key does.
    private static string NormalizePrefix(string prefix) {
        if (string.IsNullOrWhiteSpace(prefix)) {
            return "/";
        }

        return NormalizeKey(prefix);
    }

    private static string NormalizeKey(string key) {
        PagePath normalized = PagePath.Normalize(key);

        return normalized.IsAddressable
            ? normalized.Key
            : throw new ArgumentException(
                $"'{key}' cannot name a page: a key ends at '?' or '#'.", nameof(key));
    }

    private bool IsHtml(string key) =>
        contentTypes.ForPath(key).StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

    private void Charge(int chars, string what) {
        if (!budget.TryCharge(chars)) {
            throw new InvalidOperationException(
                $"Reading {what} on {path.Key} would take this render past {budget.MaxChars} characters "
                + "of page source. Raise Pages:Read:MaxSourceCharsPerRender, or read a smaller revision.");
        }
    }

    private void ThrowIfEnded() {
        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot read the page store.");
        }

        Deadline.ThrowIfCancellationRequested();
    }

    // The render's deadline, linked in by the renderer.
    private CancellationToken Cancellation() => Deadline;
}
