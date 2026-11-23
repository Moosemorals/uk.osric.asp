// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Pages;

/// <summary>The session plumbing a hand-built context needs, with the production defaults.</summary>
internal static class TestSessions {

    /// <summary>The visitor store's bounds a test starts from.</summary>
    public static VisitorOptions Options => TestOptions.Stores.Visitor;

    /// <summary>The cookie name and policy a test starts from.</summary>
    public static PageSessionCookieOptions Cookie { get; } =
        new(PageSessionCookieOptions.DefaultName, CookieSecurePolicy.SameAsRequest);

    /// <summary>The browser cookie's name, policy and lifetime a test starts from.</summary>
    public static PageBrowserCookieOptions BrowserCookie { get; } =
        new(PageBrowserCookieOptions.DefaultName, CookieSecurePolicy.SameAsRequest, TimeSpan.FromDays(365));
}
