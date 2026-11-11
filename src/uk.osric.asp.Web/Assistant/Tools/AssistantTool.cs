// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

/// <summary>One tool: what it is called, what the model is told about it, and the code that runs it.</summary>
internal abstract class AssistantTool {

    /// <summary>Everything the model is told and the rest of the wiring asks about this tool.</summary>
    public abstract ToolSpec Spec { get; }

    /// <summary>Runs the tool over arguments that have already been validated.</summary>
    /// <param name="values">The validated arguments.</param>
    /// <param name="services">The caller and the services this call may use.</param>
    /// <param name="cancellationToken">The run's token.</param>
    public abstract Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken);
}
