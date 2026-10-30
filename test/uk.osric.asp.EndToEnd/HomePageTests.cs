namespace uk.osric.asp.EndToEnd;

using Microsoft.Playwright;
using NUnit.Framework;

[TestFixture]
public class HomePageTests {
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    [SetUp]
    public async Task LaunchBrowser() {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    [TearDown]
    public async Task CloseBrowser() {
        await _browser.DisposeAsync().ConfigureAwait(false);
        _playwright.Dispose();
    }

    [Test]
    public async Task Greets_the_visitor_without_a_name() {
        IPage page = await _browser.NewPageAsync();

        await page.GotoAsync(WebServerFixture.BaseAddress.ToString());

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Hello, world!");
    }

    [Test]
    public async Task Greets_the_visitor_by_name() {
        IPage page = await _browser.NewPageAsync();

        await page.GotoAsync(new Uri(WebServerFixture.BaseAddress, "/?name=Ada").ToString());

        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Hello, Ada!");
    }
}
