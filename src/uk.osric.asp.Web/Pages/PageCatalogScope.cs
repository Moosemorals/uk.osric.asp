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
/// The only refusals are about the call itself. An argument that cannot name anything is an
/// <see cref="ArgumentException"/>, because that is a page bug; an abandoned render is an
/// <see cref="InvalidOperationException"/>; and a cap is an <see cref="InvalidOperationException"/>
/// that names the setting rather than a truncated answer.
/// </para>
/// </remarks>
internal sealed class PageCatalogScope(
    PagePath path,
    IPageReadStore store,
    PageReadOptions options,
    ReadBudget budget,
    ReservedPaths reserved,
    PageContentTypes contentTypes) : RenderBoundCapability, IPageCatalog {

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PageEntry>> ListAsync(string prefix) {
        string key = NormalizePrefix(prefix);
        ThrowIfEnded();

        // One row past the cap is how the store says "there are more"; the read refuses rather than
        // showing a visitor 500 of 900 pages with nothing to say.
        IReadOnlyList<PageReadHead> rows = await store.ListAsync(key, options.MaxRowsPerList + 1, Cancellation());

        if (rows.Count > options.MaxRowsPerList) {
            throw new InvalidOperationException(
                $"Listing {key} on {path.Key} returned more than {options.MaxRowsPerList} pages. "
                + "Use a prefix to list a subtree, or raise Pages:Read:MaxRowsPerList.");
        }

        List<PageEntry> entries = new(rows.Count);
        foreach (PageReadHead row in rows) {
            entries.Add(new PageEntry(
                row.Key,
                row.SavedAt,
                row.Hash,
                row.IsDeleted,
                reserved.IsReserved(PagePath.Normalize(row.Key)),
                IsHtml(row.Key)));
        }

        return entries;
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

    // "" and "/" are the whole store; anything else is a key, so case, a trailing slash and a dot
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
