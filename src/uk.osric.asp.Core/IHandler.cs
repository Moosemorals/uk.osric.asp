// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

/// <summary>The entry point of a compiled page.</summary>
public interface IHandler {
    /// <summary>Runs the page against the request context.</summary>
    /// <param name="context">The context the page reads and writes through.</param>
    public void Handle(IContext context);
}
