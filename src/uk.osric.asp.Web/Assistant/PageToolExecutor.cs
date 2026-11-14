// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using Microsoft.Extensions.DependencyInjection;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Web.Assistant.Tools;

/// <summary>What one tool call produced, as the model receives it.</summary>
internal sealed record ToolExecutionResult(bool IsSuccess, string Content, string Summary);

/// <summary>The seam the run calls through: finds the tool, validates its arguments, and gives it the call's services.</summary>
/// <remarks>
/// Orchestration only. What a tool is and what it does is on its own type in <c>Tools</c>; this class
/// opens the per-call scope, builds the services a tool may use and hands the validated arguments over.
/// </remarks>
internal sealed class PageToolExecutor(
    IServiceScopeFactory scopeFactory,
    PagePathPolicy paths,
    AssistantOptions options,
    AssistantMetrics metrics,
    IAssistantFetcher fetcher) {

    private readonly ToolResults _results = new(options);

    /// <summary>What the model is offered, in declaration order.</summary>
    public static IReadOnlyList<ToolDefinition> Definitions => ToolRegistry.Default.Definitions;

    /// <summary>Whether the named tool changes a stored page when it succeeds.</summary>
    /// <param name="toolName">The tool the model called, as the transcript stored it.</param>
    public static bool ChangesPages(string? toolName) =>
        toolName is not null && ToolRegistry.Default.Find(toolName)?.Spec.Writes == true;

    public async Task<ToolExecutionResult> ExecuteAsync(
        string name,
        string argumentsJson,
        ToolContext context,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(context);

        if (ToolRegistry.Default.Find(name) is not { } tool) {
            string unknown = MessageText.Sanitize(name);
            return _results.Failure($"Unknown tool '{unknown}'.", $"Unknown tool {unknown}");
        }

        ToolArgumentsResult parsed = ToolArguments.Parse(argumentsJson, tool.Spec.Arguments, options.MaxToolArgumentChars);
        if (!parsed.IsSuccess) {
            return _results.Failure(parsed.Error!, $"Invalid arguments for {name}");
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ToolServices services = new(scope.ServiceProvider, context, paths, options, metrics, fetcher);
        return await tool.RunAsync(new ToolValues(parsed.Values), services, cancellationToken);
    }

    /// <summary>The refusal the path policy gives a mutating call, or null when the call may run.</summary>
    /// <remarks>
    /// The approval gate asks this before it prompts. An approval the policy has already refused is
    /// one the owner could only deny, so it is not worth their attention. An unknown tool, a tool
    /// that writes nothing or names no path, bad arguments and a permitted path all answer null,
    /// because <see cref="ExecuteAsync"/> owns those refusals and the owner may still want to see the
    /// call. <c>fetch</c> is approval-gated and has no path, so its URL is the prompt's to show.
    /// </remarks>
    /// <param name="name">The tool the model called.</param>
    /// <param name="argumentsJson">Its complete arguments, as validated.</param>
    public ToolExecutionResult? RefuseBeforeApproval(string name, string argumentsJson) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(argumentsJson);

        // Only a path-naming, approval-gated tool can have its path refused before the owner is
        // asked; fetch is approval-gated but names no path, so it falls through to the prompt.
        if (ToolRegistry.Default.Find(name) is not { Spec: { NeedsApproval: true, PathArgument: { } pathArgument } } tool) {
            return null;
        }

        ToolArgumentsResult parsed = ToolArguments.Parse(argumentsJson, tool.Spec.Arguments, options.MaxToolArgumentChars);
        if (!parsed.IsSuccess || new ToolValues(parsed.Values).String(pathArgument) is not { } pathText) {
            return null;
        }

        PathValidationResult checkedPath = paths.Validate(pathText, write: true);
        return checkedPath.IsSuccess ? null : _results.Failure(checkedPath.Error!, $"{tool.Spec.Verb} refused");
    }
}
