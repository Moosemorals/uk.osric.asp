// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;

/// <summary>The store's write capability, bound to one render.</summary>
/// <remarks>
/// <para>
/// The host offers this to every content layer and never to a layout. Which page asked is not part of
/// the check: the authority is the signed-in visitor, and the reserved keys are the only pages the
/// shell keeps out of reach.
/// </para>
/// <para>
/// Authority is asymmetric with the catalog. An anonymous render is refused with a
/// <see cref="UserFacingException"/> before anything is compiled or stored; a <c>HEAD</c>, an ended
/// render, a second write and a reserved key are page bugs and throw
/// <see cref="InvalidOperationException"/>. The key itself is the page's answer through
/// <see cref="Resolve"/>, so a typo in a query value is a message the page renders.
/// </para>
/// <para>
/// The two saves share one action: a page that saved itself cannot then edit another page in the same
/// render. A save is conditional on the head hash the form carried when it names a key, and a save that
/// has begun is not abandoned: the render's deadline bounds the wait for a compile slot, not the
/// author's edit (`pages-a-page-may-save-itself`).
/// </para>
/// </remarks>
internal sealed class PageWritesScope(
    PagePath path,
    PageEditor pages,
    PageUser? user,
    bool writable,
    ReservedPaths reserved,
    PageSeeds seeds,
    PageWriteAction action) : RenderBoundCapability, IPageWrites {

    /// <inheritdoc/>
    public PageKeyResolution Resolve(string key) {
        if (string.IsNullOrWhiteSpace(key)) {
            return new PageKeyResolution(null, false, "A page key is required.");
        }

        PagePath target = PagePath.Normalize(key);
        return target.IsAddressable
            ? new PageKeyResolution(target.Key, reserved.IsReserved(target), null)
            : new PageKeyResolution(null, false, "That is not a page address: a key cannot contain a query or a fragment.");
    }

    /// <inheritdoc/>
    public Task<string?> SeedSourceAsync(string key) {
        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("A page key is required.", nameof(key));
        }

        PagePath target = PagePath.Normalize(key);
        if (!target.IsAddressable) {
            throw new ArgumentException("The page key is not addressable.", nameof(key));
        }

        return Task.FromResult(seeds.Source(target));
    }

    /// <inheritdoc/>
    public async Task<PageSaveResult> SaveAsync(string template) {
        ArgumentNullException.ThrowIfNull(template);
        ThrowIfCannotWrite();

        // The render's token is deliberately not passed as the save's token: the deadline bounds the
        // queue for a compile slot, not the author's edit (`pages-a-page-may-save-itself`).
        SavedRevision saved = await pages.SaveAsync(path, template, CancellationToken.None, Deadline);
        Acted();

        return new PageSaveResult(saved.Revision.Hash, PageDiagnostics.ToSdk(saved.Diagnostics));
    }

    /// <inheritdoc/>
    public async Task<PageEditResult> SaveAsync(string key, string template, string? expectedHash) {
        ArgumentNullException.ThrowIfNull(template);

        PagePath target = ResolveForWrite(key);
        PageEditResult result = expectedHash is null
            ? await CreateAsync(target, template)
            : await SaveConditionalAsync(target, template, expectedHash);
        Acted();

        return result;
    }

    /// <inheritdoc/>
    public async Task<PageDeleteResult> DeleteAsync(string key) {
        PagePath target = ResolveForWrite(key);

        PageHeadSummary? head = await pages.GetHeadAsync(target, Deadline);
        bool wasLive = head is { IsDeleted: false };
        bool deleted = await pages.DeleteAsync(target, Deadline);
        Acted();

        return new PageDeleteResult(deleted, wasLive);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult?> RestoreRevisionAsync(string key, string hash, string? expectedHash) {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        PagePath target = ResolveForWrite(key);
        PageRevisionTemplate? revision = await pages.GetRevisionAsync(target, hash, Deadline);
        if (revision is null) {
            return null;
        }

        PageTemplate? live = await pages.GetAsync(target, Deadline);
        if (PageSourceChange.IsNoOp(live?.Template, revision.Template)) {
            Acted();
            return Unchanged(live?.Hash);
        }

        PageEditResult result = live is not null
            ? await SaveConditionalAsync(target, revision.Template, expectedHash)
            : await SaveUnconditionalAsync(target, revision.Template);
        Acted();

        return result;
    }

    /// <inheritdoc/>
    public async Task<PageEditResult?> AdoptSeedAsync(string key) {
        PagePath target = ResolveForWrite(key);
        SeedFile? seed = seeds.Find(target);
        if (seed is null) {
            return null;
        }

        PageHeadSummary? head = await pages.GetHeadAsync(target, Deadline);
        SeedComparison comparison = SeedComparison.Of(head?.SeedHash, seed.Hash, head?.ContentHash);

        if (head is null || head.IsDeleted) {
            throw new InvalidOperationException(
                $"{target.Key} is {Words(SeedComparison.Of(head?.SeedHash, seed.Hash, head?.ContentHash), seed.Hash)}; only a live page that already holds the seed can be recorded as installed.");
        }

        if (string.Equals(head.SeedHash, seed.Hash, StringComparison.Ordinal)) {
            PageTemplate? already = await pages.GetAsync(target, Deadline);
            Acted();
            return Unchanged(already?.Hash);
        }

        if (!comparison.CanAdopt) {
            throw new InvalidOperationException(
                $"{target.Key} is {Words(comparison, seed.Hash)}; only a page that already holds the shipped seed can be recorded as installed.");
        }

        bool adopted = await pages.AdoptAsync(target, seed.Hash, Deadline);
        PageTemplate? live = await pages.GetAsync(target, Deadline);
        Acted();

        return adopted
            ? new PageEditResult(PageEditOutcome.Saved, live?.Hash, [])
            : new PageEditResult(PageEditOutcome.Stale, null, []);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult?> InstallSeedAsync(string key, string? expectedHash) {
        PagePath target = ResolveForWrite(key);
        SeedFile? seed = seeds.Find(target);
        if (seed is null) {
            return null;
        }

        PageHeadSummary? head = await pages.GetHeadAsync(target, Deadline);
        if (head is null) {
            PageEditResult created = await SaveSeedUnconditionalAsync(target, seed);
            Acted();
            return created;
        }

        SeedComparison comparison = SeedComparison.Of(head.SeedHash, seed.Hash, head.ContentHash);

        if (comparison.Verdict == SeedVerdict.InSync) {
            PageTemplate? already = await pages.GetAsync(target, Deadline);
            Acted();
            return Unchanged(already?.Hash);
        }

        if (!comparison.CanApply) {
            throw new InvalidOperationException(
                $"{target.Key} is {Words(comparison, seed.Hash)}; the release does not overwrite it automatically.");
        }

        PageEditResult result = await SaveSeedConditionalAsync(target, seed, expectedHash);
        Acted();

        return result;
    }

    // A form rendered from a page with no live source carries no expectation, so the save applies
    // only while that is still true: a create never overwrites a page that appeared in the meantime.
    private async Task<PageEditResult> CreateAsync(PagePath target, string template) {
        PageTemplate? live = await pages.GetAsync(target, Deadline);

        return live is not null
            ? new PageEditResult(PageEditOutcome.Stale, null, [])
            : await SaveUnconditionalAsync(target, template);
    }

    private async Task<PageEditResult> SaveConditionalAsync(PagePath target, string template, string? expectedHash) {
        if (string.IsNullOrWhiteSpace(expectedHash)) {
            return new PageEditResult(PageEditOutcome.Stale, null, []);
        }

        // The render's token is deliberately not the save's token: the deadline bounds the wait for
        // a compile slot, not the author's edit (`pages-a-page-may-save-itself`).
        ConditionalPageSaveResult saved = await pages.SaveIfRevisionAsync(
            target, template, expectedHash, CancellationToken.None, Deadline);

        return saved.Saved
            ? new PageEditResult(PageEditOutcome.Saved, saved.RevisionHash, PageDiagnostics.ToSdk(saved.Diagnostics))
            : new PageEditResult(PageEditOutcome.Stale, null, []);
    }

    private async Task<PageEditResult> SaveUnconditionalAsync(PagePath target, string template) {
        SavedRevision saved = await pages.SaveAsync(target, template, CancellationToken.None, Deadline);

        return new PageEditResult(PageEditOutcome.Saved, saved.Revision.Hash, PageDiagnostics.ToSdk(saved.Diagnostics));
    }

    private async Task<PageEditResult> SaveSeedUnconditionalAsync(PagePath target, SeedFile seed) {
        SavedRevision saved = await pages.SaveSeedAsync(target, seed.Source, seed.Hash, CancellationToken.None, Deadline);

        return new PageEditResult(PageEditOutcome.Saved, saved.Revision.Hash, PageDiagnostics.ToSdk(saved.Diagnostics));
    }

    private async Task<PageEditResult> SaveSeedConditionalAsync(PagePath target, SeedFile seed, string? expectedHash) {
        if (string.IsNullOrWhiteSpace(expectedHash)) {
            return new PageEditResult(PageEditOutcome.Stale, null, []);
        }

        ConditionalPageSaveResult saved = await pages.SaveSeedIfRevisionAsync(
            target, seed.Source, expectedHash, seed.Hash, CancellationToken.None, Deadline);

        return saved.Saved
            ? new PageEditResult(PageEditOutcome.Saved, saved.RevisionHash, PageDiagnostics.ToSdk(saved.Diagnostics))
            : new PageEditResult(PageEditOutcome.Stale, null, []);
    }

    private static string Words(SeedComparison comparison, string shipped) =>
        SeedVerdictWords.Describe(comparison, shipped).Verdict;

    private static PageEditResult Unchanged(string? hash) => new(PageEditOutcome.Unchanged, hash, []);

    private PagePath ResolveForWrite(string key) {
        ThrowIfCannotWrite();

        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("A page key is required.", nameof(key));
        }

        PagePath target = PagePath.Normalize(key);
        if (!target.IsAddressable) {
            throw new ArgumentException("The page key is not addressable.", nameof(key));
        }

        if (reserved.IsReserved(target)) {
            throw new InvalidOperationException($"{target.Key} is a shell file; no page is stored there.");
        }

        return target;
    }

    // The bound save names no key, so it skips the key checks and keeps the four that every write
    // makes. The order is the writer's: a visitor state, then the two render bugs, then the action.
    private void ThrowIfCannotWrite() {
        ThrowIfAnonymous();
        ThrowIfReadOnly();
        ThrowIfEnded();
        ThrowIfActed();
    }

    private void ThrowIfAnonymous() {
        if (user is null) {
            throw new UserFacingException("Sign in to edit a page. Nothing was saved.");
        }
    }

    private void ThrowIfReadOnly() {
        if (!writable) {
            throw new InvalidOperationException($"{path.Key} may not edit a page during a HEAD request.");
        }
    }

    private void ThrowIfEnded() {
        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot edit a page.");
        }

        if (Deadline.IsCancellationRequested) {
            throw new InvalidOperationException($"{path.Key} has run out of time; it cannot edit a page.");
        }
    }

    private void ThrowIfActed() {
        if (action.HasActed) {
            throw new InvalidOperationException($"{path.Key} has already edited a page in this render.");
        }
    }

    private void Acted() => action.Act();
}
