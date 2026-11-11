// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant.Shell;

/// <summary>Runs a read-only virtual-shell command over stored pages and the shell's files.</summary>
internal sealed class ShellTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "shell",
        "Run a read-only virtual-shell command over stored pages and the shell's files. Run help for the commands, their options and their arguments. A path names a page or a folder, and grep's query is a regular expression.",
        false,
        [new("command", ToolArgumentType.String, true)]) { Verb = "Shell" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        ShellOutcome outcome = await ShellRunner.RunAsync(
            values.RequiredString("command"), services.Pages, services.Paths, services.Options, cancellationToken);

        return outcome.IsSuccess
            ? services.Results.Success(new { output = outcome.Output, truncated = outcome.Truncated }, outcome.Summary)
            : services.Results.Failure(outcome.Error!, outcome.Summary);
    }
}
