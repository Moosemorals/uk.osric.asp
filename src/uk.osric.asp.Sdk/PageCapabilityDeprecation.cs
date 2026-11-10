// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The one message both retired names in the page contract carry.</summary>
internal static class PageCapabilityDeprecation {
    internal const string Message =
        "Use IPageCapabilities and IContext.Capabilities: the page contract calls these capabilities now.";
}
