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

        // One row past the limit is how the store says "there are more" without a second query. A
        // one-level listing also asks for its folders, because a child whose only content is deeper
        // is not a head that read can see.
        PageReadSlice slice = await store.ListAsync(key, after, limit + 1, request.Recursive, Cancellation());
        IReadOnlyList<string> folders = request.Recursive
            ? []
            : await store.ListFoldersAsync(key, after, limit + 1, Cancellation());

        Dictionary<string, PageReadHead> heads = [with(StringComparer.Ordinal)];
        List<PageListingKey> facts = [];
        foreach (PageReadHead row in slice.Rows) {
            heads[row.Key] = row;
            facts.Add(new PageListingKey(row.Key, IsFolder: false));
        }

        // The route table is the shell's, and a page cannot own it: every route is a fact the fold
        // filters to the subtree, so a route row costs no read.
        foreach (PagePath route in reserved.Keys) {
            facts.Add(new PageListingKey(route.Key, IsFolder: false));
        }

        foreach (string folder in folders) {
            facts.Add(new PageListingKey(folder, IsFolder: true));
        }

        PageListingKeys answer = PageListingFold.Of(facts, key, after, request.Recursive, limit);

        List<PageEntry> entries = [];
        entries.EnsureCapacity(answer.Keys.Count);
        foreach (PageListingKey node in answer.Keys) {
            entries.Add(Describe(node, heads));
        }

        return new PageListing(entries, answer.Truncated) {
            Version = slice.Version,
            // A first call carries no token and is never changed. Any token that is not the one just
            // read is a warning, not an error: a stale token should not turn a page bug into a 500.
            Changed = request.Version is not null
                && !string.Equals(request.Version, slice.Version, StringComparison.Ordinal),
        };
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

    // One row of the answer, from the head the slice answered, the route table, or nothing but the
    // fact that something lives below the key. The marks are the host's rules and the kind says which
    // of them apply, so a key that is a page and a folder is one row rather than two.
    private PageEntry Describe(PageListingKey node, IReadOnlyDictionary<string, PageReadHead> heads) {
        PageEntryKind kind = node.IsFolder ? PageEntryKind.Folder : PageEntryKind.None;
        bool isRoute = reserved.IsReserved(PagePath.Normalize(node.Key));
        if (isRoute) {
            kind |= PageEntryKind.Route;
        }

        if (heads.TryGetValue(node.Key, out PageReadHead? head) && head is not null) {
            kind |= head.IsDeleted ? PageEntryKind.Page | PageEntryKind.Tombstone : PageEntryKind.Page;
            PageImageState state = PageImageStatus.Describe(
                head.RuntimeId, head.HasImage, head.HasDiagnostics, currentRuntimeId, head.TemplateKind);

            return new PageEntry(node.Key, kind) {
                SavedAt = head.SavedAt,
                Hash = head.Hash,
                IsHtml = IsHtml(node.Key),
                IsAsset = head.TemplateKind.IsAsset(),
                IsLibrary = head.TemplateKind.IsLibrary(),
                IsWorker = head.TemplateKind.IsWorker(),
                State = state.Describe(),
                Libraries = PageLibraryFingerprint.KeysOf(head.TemplateLibraries),
                Seed = DescribeSeed(head),
            };
        }

        // A row with no head has nothing to serve, so the content type matters only for a route.
        return new PageEntry(node.Key, kind) {
            IsHtml = isRoute && IsHtml(node.Key),
        };
    }

    // The row the store answered for, judged against the pack the process read once. A key with no
    // seed on either side is the owner's own page, and carries no summary.
    private PageSeedSummary? DescribeSeed(PageReadHead row) {
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
