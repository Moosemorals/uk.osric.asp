// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Admin;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Authentication;
using uk.osric.asp.Web.Metrics;
using uk.osric.asp.Web.Pages;

/// <summary>The options the site ships with, read through the real factories.</summary>
/// <remarks>
/// A test that needs a policy value starts from the shipped one and overrides what it is about with
/// <c>with</c>, so the suite cannot drift into a second source of values. The factories are the same
/// ones the host uses, so a shipped file the site could not start with fails here too.
/// </remarks>
internal static class TestOptions {

    private static IConfiguration Configuration => ShippedConfiguration.Read();

    /// <summary>The site origin the repository ships.</summary>
    public static SiteOptions Site { get; } = SiteOptions.FromConfiguration(Configuration);

    /// <summary>The render budget the site ships with.</summary>
    public static RenderOptions Render { get; } = RenderOptions.FromConfiguration(Configuration);

    /// <summary>The compile budget the site ships with.</summary>
    public static CompileOptions Compile { get; } = CompileOptions.FromConfiguration(Configuration);

    /// <summary>The stream limits the site ships with.</summary>
    public static StreamOptions Stream { get; } = StreamOptions.FromConfiguration(Configuration);

    /// <summary>The render budget a fixture that is not about the deadline renders under.</summary>
    /// <remarks>
    /// A fixture is not about a deadline it did not ask for. The render and compile budgets are
    /// nested, and a fixture that renders a seeded page compiles it inside both, beside every other
    /// fixture doing the same: the editor page is 31 KB and takes about two seconds to compile beside
    /// four fixtures and many more beside eight, so the five seconds the site ships is a budget a test
    /// can lose without being wrong. The deadlines keep their own tests, which pass their own value —
    /// <c>PageImageBuilderTests</c> and <c>PageRendererTests</c> use a quarter of a second,
    /// <c>RenderBudgetTests</c> one second — so nothing here asserts these two numbers. Both sit at
    /// the top of the range the configuration accepts, which is what a fixture wants and a deployment
    /// should not need.
    /// </remarks>
    public static RenderOptions FixtureRender { get; } = Render with { TimeoutSeconds = 60 };

    /// <inheritdoc cref="FixtureRender"/>
    public static CompileOptions FixtureCompile { get; } = Compile with { TimeoutSeconds = 30 };

    /// <summary>The job bounds the site ships with.</summary>
    public static PageJobOptions Jobs { get; } = PageJobOptions.FromConfiguration(Configuration);

    /// <summary>The page stores' caps and identity bounds.</summary>
    public static PageStoresOptions Stores { get; } = PageStoresOptions.FromConfiguration(Configuration);

    /// <summary>The outbound fetch limits.</summary>
    public static PageFetchOptions Fetch { get; } = PageFetchOptions.FromConfiguration(Configuration);

    /// <summary>The page store read limits.</summary>
    public static PageReadOptions Read { get; } = PageReadOptions.FromConfiguration(Configuration);

    /// <summary>The page store search limits.</summary>
    public static PageSearchOptions Search { get; } = PageSearchOptions.FromConfiguration(Configuration);

    /// <summary>A scan over the shipped search values, for a fixture that wires an environment by hand.</summary>
    public static PageSourceScan Scan() => new(Search, TimeProvider.System, new PageMetrics());

    /// <summary>The import and export bounds.</summary>
    public static ArchiveOptions Archive { get; } = ArchiveOptions.FromConfiguration(Configuration);

    /// <summary>The admin routes' values.</summary>
    public static AdminOptions Admin { get; } = AdminOptions.FromConfiguration(Configuration);

    /// <summary>The editor's key.</summary>
    public static PageEditorOptions Editor { get; } = PageEditorOptions.FromConfiguration(Configuration);

    /// <summary>The layout every code page is wrapped in.</summary>
    public static PageLayoutOptions Layout { get; } = PageLayoutOptions.FromConfiguration(Configuration);

    /// <summary>The content-type map.</summary>
    public static PageContentTypes ContentTypes { get; } =
        PageContentTypesConfiguration.FromConfiguration(Configuration, NullLogger.Instance);

    /// <summary>The keys the site's own routes answer.</summary>
    public static ReservedPaths Reserved { get; } = new(ReservedPaths.Default);

    /// <summary>The pack this release ships, read from beside the test assembly.</summary>
    public static PageSeeds Seeds { get; } = new(Path.Combine(AppContext.BaseDirectory, "Pages", "Seeds"));

    /// <summary>The source this release ships for a key, or null when it ships none.</summary>
    public static string? SeedSource(PagePath path) => Seeds.Source(path);

    /// <summary>The assistant's limits.</summary>
    /// <remarks>
    /// The key is a test value: the shipped file carries none, and the host refuses to start without
    /// one. Nothing in this suite reaches a provider, so its value cannot matter.
    /// </remarks>
    public static AssistantOptions Assistant { get; } =
        AssistantOptions.FromConfiguration(ShippedConfiguration.Read(("Assistant:ApiKey", "test-key")));

    /// <summary>The content budget the provider's own documented example supports.</summary>
    /// <remarks>
    /// One model's window, reported by the provider's model list: deepseek-flash, 1,048,576 tokens of
    /// window and 393,216 of output. A test that is about a content cap asks for
    /// <see cref="BudgetOfResultChars"/> instead, because the cap is a fraction of this and not a
    /// setting of its own.
    /// </remarks>
    public static ContextBudget Budget { get; } = ContextBudget.Create(1_048_576, 393_216, 131_072);

    /// <summary>A budget whose one tool result is exactly <paramref name="characters"/> characters.</summary>
    /// <param name="characters">The result cap the fixture is about.</param>
    /// <returns>The budget that produces it.</returns>
    public static ContextBudget BudgetOfResultChars(long characters) =>
        ContextBudget.ForPromptShare(characters * 5, maximumOutputTokens: 393_216);

    /// <summary>The sign-in policy.</summary>
    public static AuthenticationOptions Authentication { get; } = AuthenticationOptions.FromConfiguration(Configuration);

    /// <summary>The metrics settings.</summary>
    public static MetricsOptions Metrics { get; } = MetricsOptions.FromConfiguration(Configuration);

}
