// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>A visitor's messages: durable, addressed, and taken once.</summary>
/// <remarks>
/// <para>
/// A message is one row addressed to a page or a named topic and owned by the reader's subject. A
/// post names the destination page, or a <see cref="PageTopic"/> when there is no page behind the
/// queue; a take or a peek reads from the bound page, or from a topic.
/// </para>
/// <para>
/// A take removes the message as it answers, in one statement, so the same message is never handed
/// to two renders. A peek answers the message a take would answer right now and removes nothing: it
/// is a preview, not a claim, and another render may take that row before this one acts on it. Both
/// answer <see cref="PageMessageRead"/>, and a take or peek that found nothing answers
/// <see cref="PageMessageRefusal.Empty"/>.
/// </para>
/// <para>
/// Everything the page got wrong throws: a topic that cannot name a queue, a lifetime a Visitor
/// subject cannot carry, a body over its limit, and a write during a <c>HEAD</c>, an abandoned render
/// or a passed deadline. A destination that cannot receive a message is a value, because it depends
/// on the page store rather than on the page's code.
/// </para>
/// </remarks>
public interface IMessageStore {

    /// <summary>Posts a message to another page's queue.</summary>
    /// <param name="toPageKey">The destination page's key. It is normalised before use.</param>
    /// <param name="body">The message; ordinal text, at most the configured body limit in bytes.</param>
    /// <param name="lifetime">
    /// The message's lifetime, or <see langword="null"/> for <see cref="StoreLifetime.Person"/>. A
    /// <c>Page</c> or <c>NextRead</c> lifetime is a page bug and throws; an expiry composes with the
    /// default.
    /// </param>
    /// <returns>Whether it was queued, or why the destination cannot receive it.</returns>
    /// <exception cref="System.ArgumentException">The body or the lifetime breaks a rule or exceeds a limit.</exception>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
#pragma warning disable RS0026 // Two addresses, one optional lifetime each: the first parameter's type makes the call unambiguous.
    public Task<PageMessagePostResult> PostAsync(string toPageKey, string body, StoreLifetime? lifetime = null);

    /// <summary>Posts a message to a named topic, which needs no page behind it.</summary>
    /// <param name="topic">The queue's name, which the page constructed.</param>
    /// <param name="body">The message; ordinal text, at most the configured body limit in bytes.</param>
    /// <param name="lifetime">The message's lifetime, or <see langword="null"/> for <see cref="StoreLifetime.Person"/>.</param>
    /// <returns>When queued, <see cref="PageMessageRefusal.None"/>; the queue holds no address to refuse.</returns>
    /// <exception cref="System.ArgumentException">The body or the lifetime breaks a rule or exceeds a limit.</exception>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
    public Task<PageMessagePostResult> PostAsync(PageTopic topic, string body, StoreLifetime? lifetime = null);
#pragma warning restore RS0026

    /// <summary>Takes the oldest message addressed to the bound page, removing it.</summary>
    /// <returns>The message, or <see cref="PageMessageRefusal.Empty"/> when none is waiting.</returns>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
    public Task<PageMessageRead> TakeAsync();

    /// <summary>Takes the oldest message on a named topic, removing it.</summary>
    /// <param name="topic">The queue's name.</param>
    /// <returns>The message, or <see cref="PageMessageRefusal.Empty"/> when none is waiting.</returns>
    /// <exception cref="System.InvalidOperationException">The render cannot write: a <c>HEAD</c>, an abandoned render or a passed deadline.</exception>
    public Task<PageMessageRead> TakeAsync(PageTopic topic);

    /// <summary>Reads the message a take would answer now, removing nothing.</summary>
    /// <returns>The message, or <see cref="PageMessageRefusal.Empty"/> when none is waiting.</returns>
    /// <remarks>A <c>HEAD</c> may peek, and so may a layout: a peek changes nothing.</remarks>
    public Task<PageMessageRead> PeekAsync();

    /// <summary>Reads the message a take on a topic would answer now, removing nothing.</summary>
    /// <param name="topic">The queue's name.</param>
    /// <returns>The message, or <see cref="PageMessageRefusal.Empty"/> when none is waiting.</returns>
    public Task<PageMessageRead> PeekAsync(PageTopic topic);
}
