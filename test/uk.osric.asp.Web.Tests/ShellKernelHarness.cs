// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Web.Assistant;
using uk.osric.asp.Web.Assistant.Shell;
using uk.osric.asp.Web.Editor;
using uk.osric.asp.Web.Pages;

/// <summary>Runs a shell line against an in-memory store, the way the tool does.</summary>
internal static class ShellKernelHarness {

    public static readonly PagePathPolicy Policy = new(
        new ReservedPaths(ReservedPaths.Default),
        TestOptions.Assistant);

    /// <summary>Runs a line through the kernel, or fails the test when the command is not an applet.</summary>
    public static async Task<ShellOutcome> RunAsync(PageEditor pages, string line, int maxChars = 4096) =>
        await ShellRunner.RunAsync(
            line, pages, Policy, TestOptions.Assistant with { MaxToolResultChars = maxChars }, CancellationToken.None);

    /// <summary>The answer, for a line that is expected to work.</summary>
    public static async Task<string> OutputAsync(PageEditor pages, string line) {
        ShellOutcome outcome = await RunAsync(pages, line);

        Assert.That(outcome.IsSuccess, Is.True, outcome.Error);
        return outcome.Output;
    }

    /// <summary>The refusal, for a line that is expected not to.</summary>
    public static async Task<string> RefusalAsync(PageEditor pages, string line) {
        ShellOutcome outcome = await RunAsync(pages, line);

        Assert.That(outcome.IsSuccess, Is.False, "expected a refusal");
        return outcome.Error!;
    }
}
