// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using System.Runtime.CompilerServices;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Shell;

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
/// <see cref="ArgumentException"/>, because that is a page bug; a limit past the limit is an
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
    PageSourceScan search,
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
    public async Task<PageSearch> SearchAsync(PageSearchRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.Pattern);

        string prefix = NormalizePrefix(request.Prefix);

        int limit = request.Limit ?? search.Options.DefaultMatches;
        if (limit < 1 || limit > search.Options.MaxMatches) {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                limit,
                $"A search of {path.Key} asked for {limit} matches; a limit is 1 to {search.Options.MaxMatches}. "
                + "Raise Pages:Search:MaxMatches, or ask for a slice and resume from the last Cursor.");
        }

        int context = request.Context ?? search.Options.DefaultContextBytes;
        if (context < 1 || context > search.Options.MaxContextBytes) {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                context,
                $"A search of {path.Key} asked for a {context}-byte snippet; a context is 1 to "
                + $"{search.Options.MaxContextBytes}. Raise Pages:Search:MaxContextBytes, or read the page for the rest of a line.");
        }

        PageSearchCursor? after = request.After is { } cursor ? NormalizeCursor(cursor) : null;

        if (!GrepPattern.TryCompile(
                request.Pattern,
                request.Kind == PageSearchPatternKind.Literal,
                request.IgnoreCase,
                out GrepPattern? compiled,
                out string? error)) {
            throw new ArgumentException(
                $"{error} A search reads the text as it stands, or as a basic regular expression in which "
                + "^ $ . * and [abc] are metacharacters, \\| is either-this-or-that, and every other character is itself.",
                nameof(request));
        }

        ThrowIfEnded();

        PageScanResult result = await search.RunAsync(
            new PageScanQuery(compiled!, limit, after, request.Version, TemplateLines.Split, PageSearchCaller.Page),
            token => store.VersionAsync(prefix, token),
            (from, token) => SourcesAsync(prefix, request.Recursive, from, token),
            Cancellation());

        List<PageSearchMatch> matches = [];
        foreach (PageScanMatch match in result.Matches) {
            LineSnippet snippet = LineSnippet.Of(match.Text, match.Column - 1, match.Length, context);
            matches.Add(new PageSearchMatch(
                match.Key, match.Line, match.Column, snippet.Text, snippet.Start + 1, snippet.Clipped));
        }

        return new PageSearch(matches, result.Cursor, result.End != PageSearchEnd.Exhausted) {
            Version = result.Version,
            Changed = result.Changed,
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

        Charge(Utf8.SizeInBytes(source.Template), source.Key);

        return new PageSource(source.Key, source.Hash, source.SavedAt, source.Template);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageRevisionInfo>> RevisionsAsync(string key) {
        string normalized = NormalizeKey(key);
        ThrowIfEnded();

        IReadOnlyList<PageReadRevision> revisions = await store.ListRevisionsAsync(normalized, Cancellation());

        return [.. revisions.Select(revision =>
            new PageRevisionInfo(revision.Hash, revision.SavedAt, revision.IsCurrent) { SourceBytes = revision.SourceBytes })];
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

        Charge(Utf8.SizeInBytes(source.Template), $"{source.Key} at {source.Hash}");

        return new PageSource(source.Key, source.Hash, source.SavedAt, source.Template);
    }

    // The scan's source set: the store's live pages and the shell's route records, merged in key order.
    // A route wins the key it answers, as PageStoreReader's read does, so one key is one source. It is
    // stepped rather than materialised, so the allowance covers the reading as well as the matching.
    private async IAsyncEnumerable<PageSourceText> SourcesAsync(
        string prefix,
        bool recursive,
        PageSearchCursor? from,
        [EnumeratorCancellation] CancellationToken cancellationToken) {
        PagePath at = PagePath.Normalize(prefix);
        PageScope scope = new(at, IncludeSelf: true, Recursive: recursive);

        List<PagePath> routes = [.. reserved.Keys
            .Where(route => scope.Contains(route))
            .Where(route => from is null || string.CompareOrdinal(route.Key, from.Value.Key) >= 0)
            .OrderBy(route => route.Key, StringComparer.Ordinal)];

        int nextRoute = 0;
        await foreach (PageReadSource source in store.ScanAsync(at.Key, from?.Key, recursive, cancellationToken)) {
            while (nextRoute < routes.Count && string.CompareOrdinal(routes[nextRoute].Key, source.Key) < 0) {
                PagePath route = routes[nextRoute++];
                yield return new PageSourceText(route.Key, ShellCatalogue.Text(route));
            }

            if (nextRoute < routes.Count && string.Equals(routes[nextRoute].Key, source.Key, StringComparison.Ordinal)) {
                PagePath route = routes[nextRoute++];
                yield return new PageSourceText(route.Key, ShellCatalogue.Text(route));
                continue;
            }

            yield return new PageSourceText(source.Key, source.Template);
        }

        while (nextRoute < routes.Count) {
            PagePath route = routes[nextRoute++];
            yield return new PageSourceText(route.Key, ShellCatalogue.Text(route));
        }
    }

    private static PageSearchCursor NormalizeCursor(PageSearchCursor cursor) {
        string key = NormalizeKey(cursor.Key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cursor.Line);

        return new PageSearchCursor(key, cursor.Line);
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
                SourceBytes = head.SourceBytes,
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

    private void Charge(int bytes, string what) {
        if (!budget.TryCharge(bytes)) {
            throw new InvalidOperationException(
                $"Reading {what} on {path.Key} was refused: {budget.DescribeRefusal()}. "
                + "Raise the rate, or read a smaller revision.");
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
