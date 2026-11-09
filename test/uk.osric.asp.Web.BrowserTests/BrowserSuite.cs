// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.BrowserTests;

using Microsoft.Playwright;

/// <summary>
/// Starts the host and the browser once for the whole suite. One host keeps the suite to a handful of
/// seconds; fixtures that need different assistant limits start their own.
/// </summary>
[SetUpFixture]
[NonParallelizable]
internal sealed class BrowserSuite {

    private static IPlaywright? s_playwright;

    /// <summary>The browser every test drives; Firefox, because that is the browser the site is read in.</summary>
    public static IBrowser Browser { get; private set; } = null!;

    /// <summary>The host under test.</summary>
    public static BrowserHost Host { get; private set; } = null!;

    /// <summary>A browser state file for an owner who has signed in, so a test starts signed in.</summary>
    public static string StorageStatePath { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartAsync() {
        Host = await BrowserHost.SharedAsync();
        s_playwright = await Playwright.CreateAsync();
        Browser = await s_playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions {
            Headless = Environment.GetEnvironmentVariable("HEADFUL") is not "1",
        });
        StorageStatePath = await SignInOnceAsync();
    }

    // The suite signs in through the real form once; every test starts from that state, so the suite
    // does not spend the sign-in throttle's whole allowance on itself.
    private static async Task<string> SignInOnceAsync() {
        await using IBrowserContext context = await Browser.NewContextAsync(new BrowserNewContextOptions {
            BaseURL = Host.BaseAddress.ToString(),
            IgnoreHTTPSErrors = true,
        });
        IPage page = await context.NewPageAsync();
        await page.GotoAsync("login");
        await page.FillAsync("#username", BrowserHost.UserName);
        await page.FillAsync("#password", BrowserHost.Password);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync("**/edit");

        string state = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.signed-in.{Guid.NewGuid():N}.json");
        await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = state });
        return state;
    }

    [OneTimeTearDown]
    public async Task StopAsync() {
        if (Browser is not null) {
            await Browser.CloseAsync();
        }
        s_playwright?.Dispose();
        await BrowserHost.StopSharedAsync();
        if (StorageStatePath.Length > 0) {
            File.Delete(StorageStatePath);
        }
    }
}
