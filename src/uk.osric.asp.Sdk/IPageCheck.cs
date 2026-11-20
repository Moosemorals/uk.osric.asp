// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Answers "would this source check" for text the caller already holds.</summary>
/// <remarks>
/// <para>
/// The source is the subject: there is no key, no read of a stored page and no write. A caller that
/// wants a saved page checked reads it with <see cref="IPageCatalog.ReadAsync"/> and passes the
/// template, and a source that has never been saved is checked exactly as one that has. A check
/// stores no revision, head, image, closure fingerprint or seed record, and recompiles nothing.
/// </para>
/// <para>
/// The language is named from the closed set in <see cref="PageCheckLanguages"/>; the host refuses a
/// name it does not answer for. A template is generation, the page API policy and the compiler
/// against the store's libraries, and another language is another tier behind the same value, so the
/// caller reads a <see cref="PageCheckResult"/> whichever one ran.
/// </para>
/// <para>
/// It is offered only when the render has a signed-in visitor, because a compile is a shared
/// resource and a check spends one. It takes a slot from the compile budget the site already has;
/// a check that waits past the render's deadline, or past the compile timeout, answers a
/// <c>PAGE010</c> error rather than hanging. An abandoned render's check answers the same.
/// </para>
/// <para>
/// The call returns a <see cref="Task"/> and takes no cancellation token, as every page read does:
/// the render owns <see cref="IContext.Aborted"/> and the deadline.
/// </para>
/// </remarks>
public interface IPageCheck {

    /// <summary>Checks a source in the language the caller names.</summary>
    /// <param name="language">One of <see cref="PageCheckLanguages.Names"/>.</param>
    /// <param name="source">The complete source to check, as its author wrote it.</param>
    /// <returns>What the source would be, whether it is valid, and what the checker found.</returns>
    /// <exception cref="ArgumentException">The language is not one a check answers for.</exception>
    public Task<PageCheckResult> CheckAsync(string language, string source);
}
