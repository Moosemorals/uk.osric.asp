// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// One browser test: its own context and page, a fresh provider script, and a failing assertion for
/// anything the browser complained about while the test ran.
/// </summary>
internal abstract partial class BrowserTest {

    private readonly List<string> _problems = [];

    /// <summary>The host under test; a fixture that needs different limits starts its own.</summary>
    protected virtual BrowserHost Host => BrowserSuite.Host;

    /// <summary>The page this test drives.</summary>
    protected IPage Page { get; private set; } = null!;

    /// <summary>The browser context this test owns.</summary>
    protected IBrowserContext Context { get; private set; } = null!;

    /// <summary>The scripted provider the host answers from.</summary>
    protected StubProvider Provider => Host.Provider
        ?? throw new InvalidOperationException("This host talks to a real provider, so there is nothing to script.");

    [SetUp]
    public async Task OpenPageAsync() {
        // NUnit runs every test in one instance here, so a complaint from the last page would
        // otherwise fail this test too, and one real console error would cascade through the suite.
        _problems.Clear();
        Context = await BrowserSuite.Browser.NewContextAsync(new BrowserNewContextOptions {
            BaseURL = Host.BaseAddress.ToString(),
            IgnoreHTTPSErrors = true,
            StorageStatePath = Host == BrowserSuite.Host ? BrowserSuite.StorageStatePath : null,
        });
        await Context.AddInitScriptAsync("""
            window.__cspViolations = [];
            document.addEventListener('securitypolicyviolation', event => {
                window.__cspViolations.push(`${event.effectiveDirective} ${event.blockedURI}`);
            });
            """);
        Page = await Context.NewPageAsync();
        Page.Console += (_, message) => {
            // Firefox logs the in-flight event stream as interrupted whenever the page navigates,
            // which is the page's own reload after a run; that is the design, not a defect.
            if (message.Type == "error" && !message.Text.Contains("was interrupted while the page was loading", StringComparison.Ordinal)) {
                _problems.Add($"console error: {message.Text}");
            }
        };
        Page.PageError += (_, error) => _problems.Add($"page error: {error}");
        Host.Provider?.ForgetRequests();
    }

    [TearDown]
    public async Task ClosePageAsync() {
        try {
            if (Page is not null && !Page.IsClosed) {
                IReadOnlyList<string> violations = await Page.EvaluateAsync<string[]>("window.__cspViolations ?? []");
                _problems.AddRange(violations.Select(violation => $"content security policy: {violation}"));
            }
        } catch (PlaywrightException) {
            // The page went away with the test; there is nothing left to ask.
        }

        if (Context is not null) {
            await Context.CloseAsync();
        }

        if (_problems.Count > 0) {
            Host.WriteDiagnostics(Path.Combine(TestContext.CurrentContext.WorkDirectory, "browser-logs"));
            Assert.Fail($"The browser reported {_problems.Count} problem(s):{Environment.NewLine}{string.Join(Environment.NewLine, _problems)}{Environment.NewLine}{Environment.NewLine}Host log:{Environment.NewLine}{Host.Log}");
        }
    }

    /// <summary>Signs in through the real form, for a fixture whose host the suite did not sign into.</summary>
    protected async Task SignInAsync() {
        await Page.GotoAsync("login");
        await Page.FillAsync("#username", BrowserHost.UserName);
        await Page.FillAsync("#password", BrowserHost.Password);
        await Page.ClickAsync("button[type=submit]");
        // Signing in lands on the site root; the editor is a page, so a test asks for it by name.
        await Page.WaitForURLAsync("**/");
    }

    /// <summary>Saves <paramref name="template"/> as the page at <paramref name="path"/>, creating it if needed.</summary>
    protected async Task SavePageAsync(string path, string template) {
        await Page.GotoAsync($"pages?edit={Uri.EscapeDataString(path)}");
        await Page.FillAsync("#template", template);
        await Page.ClickAsync("button[name=cmd][value=save]");
        await ExpectMessageAsync("Saved as revision");
    }

    /// <summary>Asserts the editor's session message, which is where a save reports its result.</summary>
    protected async Task ExpectMessageAsync(string expected) {
        await Assertions.Expect(Page.Locator("main")).ToContainTextAsync(expected);
    }

    /// <summary>Opens a new conversation and returns its id.</summary>
    protected async Task<long> NewConversationAsync() {
        await Page.GotoAsync("assistant");
        await Page.ClickAsync("text=New conversation");
        await Page.WaitForURLAsync(AssistantUrl());

        return long.Parse(Page.Url.Split('/')[^1], CultureInfo.InvariantCulture);
    }

    /// <summary>Types a message and lets the page's script post it, which starts the run.</summary>
    protected async Task SendAsync(string message) {
        await Page.FillAsync("#assistant-message", message);
        await Page.ClickAsync("#assistant-send");
    }

    /// <summary>A page path that belongs to this test alone.</summary>
    protected static string UniquePath(string stem) => $"/{stem}-{Guid.NewGuid():N}";

    /// <summary>
    /// A template a stored page can be served from: the default layout wraps an HTML page with code
    /// and refuses one that never sets a title, and the layout draws the chrome but not the heading,
    /// so the page writes its own.
    /// </summary>
    /// <param name="title">The document title and the page's heading, as plain text.</param>
    /// <param name="body">Markup the test asserts on, appended after the heading.</param>
    protected static string PageTemplate(string title, params string[] body) =>
        $"<% ctx.Capabilities.GetRequired<IPageLayout>().Title = \"{title}\"; %><h1>{title}</h1>{string.Concat(body)}";

    [GeneratedRegex(@"/assistant/\d+$")]
    protected static partial Regex AssistantUrl();
}
