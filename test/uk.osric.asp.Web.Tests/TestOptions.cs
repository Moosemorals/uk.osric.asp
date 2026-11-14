// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>The render budget the site ships with.</summary>
    public static RenderOptions Render { get; } = RenderOptions.FromConfiguration(Configuration);

    /// <summary>The compile budget the site ships with.</summary>
    public static CompileOptions Compile { get; } = CompileOptions.FromConfiguration(Configuration);

    /// <summary>The page store's limits.</summary>
    public static PageStorageOptions Storage { get; } = PageStorageOptions.FromConfiguration(Configuration);

    /// <summary>The session limits.</summary>
    public static PageSessionOptions Session { get; } = PageSessionOptions.FromConfiguration(Configuration);

    /// <summary>The outbound fetch limits.</summary>
    public static PageFetchOptions Fetch { get; } = PageFetchOptions.FromConfiguration(Configuration);

    /// <summary>The page store read limits.</summary>
    public static PageReadOptions Read { get; } = PageReadOptions.FromConfiguration(Configuration);

    /// <summary>The import and export bounds.</summary>
    public static ArchiveOptions Archive { get; } = ArchiveOptions.FromConfiguration(Configuration);

    /// <summary>The admin routes' values.</summary>
    public static AdminOptions Admin { get; } = AdminOptions.FromConfiguration(Configuration);

    /// <summary>The editor's key.</summary>
    public static PageEditorOptions Editor { get; } = PageEditorOptions.FromConfiguration(Configuration);

    /// <summary>The layout every code page is wrapped in.</summary>
    public static PageLayoutConfiguration Layout { get; } = PageLayoutConfiguration.FromConfiguration(Configuration);

    /// <summary>The content-type map.</summary>
    public static PageContentTypes ContentTypes { get; } =
        PageContentTypesConfiguration.FromConfiguration(Configuration, NullLogger.Instance);

    /// <summary>The keys the site's own routes answer.</summary>
    public static ReservedPaths Reserved { get; } = new(ReservedKeys());

    /// <summary>The assistant's limits.</summary>
    public static AssistantOptions Assistant { get; } = AssistantOptions.FromConfiguration(Configuration);

    /// <summary>The sign-in policy.</summary>
    public static AuthenticationOptions Authentication { get; } = AuthenticationOptions.FromConfiguration(Configuration);

    /// <summary>The metrics settings.</summary>
    public static MetricsOptions Metrics { get; } = MetricsOptions.FromConfiguration(Configuration);

    /// <summary>The reserved keys exactly as the shipped file lists them.</summary>
    public static string[] ReservedKeys() =>
        Configuration.GetSection("Pages:ReservedPaths").Get<string[]>() ?? [];
}
