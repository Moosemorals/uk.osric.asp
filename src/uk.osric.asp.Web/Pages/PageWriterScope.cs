// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Editor;

/// <summary>One content layer's <see cref="IPageWriter"/>, bound to that layer's key.</summary>
/// <remarks>
/// <para>
/// The handle is the isolation: it has no key parameter and always saves <c>path</c>, so a page
/// cannot reach another page and a reserved key cannot be reached at all, because a reserved key
/// never renders (DEC-189).
/// </para>
/// <para>
/// Authority is asymmetric with the read on purpose. The read answers whoever the page answers for
/// (DEC-169); a write changes what the site serves, so it needs a signed-in visitor and refuses an
/// anonymous render before anything is compiled or stored (DEC-190). That refusal is a
/// <see cref="UserFacingException"/> because it is the visitor's state rather than a page bug; a
/// <c>HEAD</c>, an ended render and a second save are page bugs and are
/// <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// A save that has begun is never abandoned: the render's deadline bounds the wait for a compile
/// slot, and the compile and the insert are left to finish, because losing the author's edit is the
/// loss this site refuses (DEC-191).
/// </para>
/// </remarks>
internal sealed class PageWriterScope(
    PagePath path,
    PageSaver saver,
    PageUser? user,
    bool writable) : RenderBoundCapability, IPageWriter {

    private int _saved;

    /// <inheritdoc/>
    public async Task<PageSaveResult> SaveAsync(string template) {
        ArgumentNullException.ThrowIfNull(template);

        ThrowIfAnonymous();
        ThrowIfReadOnly();
        ThrowIfEnded();
        ThrowIfAlreadySaved();

        // The render's token is deliberately not passed as the save's token: the deadline bounds the
        // queue for a compile slot, not the author's edit (DEC-191).
        SavedRevision saved = await saver.SaveAsync(path, template, CancellationToken.None, Deadline);

        Volatile.Write(ref _saved, 1);

        return new PageSaveResult(saved.Revision.Hash, PageDiagnostics.ToSdk(saved.Diagnostics));
    }

    // The visitor's state, not a page bug: a session that expired between the form and the post is
    // the visitor's to fix, so the shell shows this sentence rather than a generic failure (DEC-190).
    private void ThrowIfAnonymous() {
        if (user is null) {
            throw new UserFacingException("Sign in to save a page. Nothing was saved.");
        }
    }

    // A HEAD runs the page to produce the representation and its validators, so a save would append a
    // revision once per validator, exactly as a state write would (DEC-125).
    private void ThrowIfReadOnly() {
        if (!writable) {
            throw new InvalidOperationException($"{path.Key} may not save itself during a HEAD request.");
        }
    }

    private void ThrowIfEnded() {
        if (IsAbandoned) {
            throw new InvalidOperationException($"The render of {path.Key} was abandoned; it cannot save itself.");
        }

        if (Deadline.IsCancellationRequested) {
            throw new InvalidOperationException($"{path.Key} has run out of time; it cannot save itself.");
        }
    }

    // One render, one save: a second has nothing to say that the first did not, and it would queue
    // for another compile. The writer is offered to the content layer alone, so this is one per
    // request in the whole chain (DEC-195).
    private void ThrowIfAlreadySaved() {
        if (Volatile.Read(ref _saved) != 0) {
            throw new InvalidOperationException($"{path.Key} has already saved itself in this render.");
        }
    }
}
