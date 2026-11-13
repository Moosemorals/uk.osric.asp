// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;

/// <summary>The editor page's <see cref="IPageEditor"/>, bound to one render.</summary>
/// <remarks>
/// <para>
/// The host offers this to the content layer whose key is the configured editor key and to nothing
/// else: not to a layout, not to another page, and not while the editor is served at any other key.
/// The handle names a key on every call because it must — an editor edits whatever the owner opens —
/// so the isolation is the registration, not the handle.
/// </para>
/// <para>
/// Authority is asymmetric with the catalog. An anonymous render is refused with a
/// <see cref="UserFacingException"/> before anything is compiled or stored; a <c>HEAD</c>, an ended
/// render, a second action and a reserved key are page bugs and throw
/// <see cref="InvalidOperationException"/>. The key itself is the page's answer through
/// <see cref="Resolve"/>, so a typo in a query value is a message the page renders.
/// </para>
/// <para>
/// A save is conditional on the head hash the form carried, and a save that has begun is not
/// abandoned: the render's deadline bounds the wait for a compile slot, not the author's edit
/// (DEC-191).
/// </para>
/// </remarks>
internal sealed class PageEditorScope(
    PagePath path,
    PageEditor pages,
    string editorKey,
    PageUser? user,
    bool writable,
    ReservedPaths reserved) : RenderBoundCapability, IPageEditor {

    private int _acted;

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
    public IReadOnlyList<string> SeedPaths() => [.. PageSeedInstaller.SeedPaths().Select(seed => seed.Key)];

    /// <inheritdoc/>
    public Task<string?> SeedSourceAsync(string key) {
        if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("A page key is required.", nameof(key));
        }

        PagePath target = PagePath.Normalize(key);
        if (!target.IsAddressable) {
            throw new ArgumentException("The page key is not addressable.", nameof(key));
        }

        return PageAssetSeeder.ReadSeedTemplateAsync(target, Deadline);
    }

    /// <inheritdoc/>
    public async Task<PageEditResult> SaveAsync(string key, string template, string expectedHash) {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);

        PagePath target = ResolveForWrite(key);
        PageEditResult result = await SaveConditionalAsync(target, template, expectedHash);
        Acted();

        return result;
    }

    /// <inheritdoc/>
    public async Task<PageDeleteResult> DeleteAsync(string key) {
        PagePath target = ResolveForWrite(key);
        if (string.Equals(target.Key, editorKey, StringComparison.Ordinal)) {
            throw new InvalidOperationException("The editor page cannot delete itself.");
        }

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
    public async Task<PageEditResult?> InstallSeedAsync(string key, string? expectedHash) {
        PagePath target = ResolveForWrite(key);
        string? source = await PageAssetSeeder.ReadSeedTemplateAsync(target, Deadline);
        if (source is null) {
            return null;
        }

        PageHeadSummary? head = await pages.GetHeadAsync(target, Deadline);
        if (head is not { IsDeleted: false }) {
            PageEditResult created = await SaveUnconditionalAsync(target, source);
            Acted();
            return created;
        }

        PageTemplate? live = await pages.GetAsync(target, Deadline);
        if (PageSourceChange.IsNoOp(live?.Template, source)) {
            Acted();
            return Unchanged(live?.Hash);
        }

        PageEditResult result = await SaveConditionalAsync(target, source, expectedHash);
        Acted();

        return result;
    }

    private async Task<PageEditResult> SaveConditionalAsync(PagePath target, string template, string? expectedHash) {
        if (string.IsNullOrWhiteSpace(expectedHash)) {
            return new PageEditResult(PageEditOutcome.Stale, null, []);
        }

        // The render's token is deliberately not the save's token: the deadline bounds the wait for
        // a compile slot, not the author's edit (DEC-191).
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

    private static PageEditResult Unchanged(string? hash) => new(PageEditOutcome.Unchanged, hash, []);

    private PagePath ResolveForWrite(string key) {
        ThrowIfAnonymous();
        ThrowIfReadOnly();
        ThrowIfEnded();
        ThrowIfActed();

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
        if (Volatile.Read(ref _acted) != 0) {
            throw new InvalidOperationException($"{path.Key} has already edited a page in this render.");
        }
    }

    private void Acted() => Interlocked.Exchange(ref _acted, 1);
}
