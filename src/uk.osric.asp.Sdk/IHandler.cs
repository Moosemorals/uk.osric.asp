// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The entry point of a compiled page.</summary>
public interface IHandler {
    /// <summary>Runs the page against the request context.</summary>
    /// <param name="context">The context the page reads and writes through.</param>
    /// <returns>A task that completes when the page has finished.</returns>
    /// <remarks>
    /// The host awaits the returned task; the response is shaped from the context once it completes.
    /// A page that calls <see cref="IContext.FlushAsync"/> commits the response earlier, and the rest
    /// of the body is sent as it is written.
    /// A page must not start work it does not await, because nothing else in the render waits for it
    /// and the answer may already have been given when it finishes.
    /// </remarks>
    public Task HandleAsync(IContext context);
}
