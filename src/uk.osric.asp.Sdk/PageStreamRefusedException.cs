// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>Thrown when the host cannot start a stream because every stream slot is taken.</summary>
/// <remarks>
/// <para>
/// The throw happens before the response is committed, so the page can still set a status, a content
/// type and a body, and the shell answers <c>503 Service Unavailable</c> when the page does not catch
/// it. A page that would rather explain the wait itself catches this and renders an ordinary document.
/// </para>
/// <para>
/// It derives from <see cref="InvalidOperationException"/> because that is the exception
/// <see cref="IContext.FlushAsync"/> documents, so a page written against the old contract still
/// catches it.
/// </para>
/// </remarks>
public sealed class PageStreamRefusedException(string message) : InvalidOperationException(message) {
}
