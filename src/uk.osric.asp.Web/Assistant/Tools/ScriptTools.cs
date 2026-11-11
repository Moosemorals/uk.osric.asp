// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Runtime;
using uk.osric.asp.Web.Pages;

/// <summary>Checks the JavaScript a stored page serves.</summary>
internal sealed class CheckScriptTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "check_script",
        "Check the JavaScript a stored page serves. Reads the page by path and parses its literal "
        + "text as an ES module. The key's extension is not consulted: a page that is not "
        + "JavaScript will generally produce syntax errors. Checks syntax only; imports are not "
        + "resolved and nothing is executed. A shell file has no page source and is refused.",
        false,
        [new("path", ToolArgumentType.String, true)]) { Verb = "Check" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        (PagePath? resolved, ToolExecutionResult? refusal) = ToolPaths.ResolveRead(Spec, values, "path", services);
        if (refusal is not null) {
            return refusal;
        }

        PagePath path = resolved!;
        if (ToolPaths.RequirePageSource(Spec, path, "reads page source", services) is { } noSource) {
            return noSource;
        }

        PageTemplate? page = await services.Pages.GetAsync(path, cancellationToken);
        if (page is null) {
            return services.Results.Failure($"Page {ToolPaths.Describe(path)} was not found.", $"Check {ToolPaths.Describe(path)}: not found");
        }

        return CheckTemplate(services, page.Template, path);
    }

    internal static ToolExecutionResult CheckTemplate(ToolServices services, string template, PagePath? path) {
        ScriptSkeletonResult skeleton = TemplateScriptSkeleton.Create(template);
        IReadOnlyList<PageDiagnostic> diagnostics = skeleton.IsSuccess
            ? JavaScriptValidator.Validate(skeleton.Source)
            : skeleton.Diagnostics;
        bool valid = diagnostics.All(diagnostic => diagnostic.Severity != PageDiagnosticSeverity.Error);

        string where = path is null ? "supplied text" : ToolPaths.Describe(path);
        string summary = diagnostics.Count == 0
            ? $"Checked {where}: valid"
            : $"Checked {where}: {diagnostics.Count} problem{(diagnostics.Count == 1 ? string.Empty : "s")}";

        // A skeleton that could not be built is reported beside the parser's own errors, so a page
        // with an unterminated template block says so instead of failing the tool call.
        return path is null
            ? services.Results.Success(new { valid, diagnostics = ScriptDiagnostics(diagnostics) }, summary)
            : services.Results.Success(new { path = path.Key, valid, diagnostics = ScriptDiagnostics(diagnostics) }, summary);
    }

    private static object[] ScriptDiagnostics(IReadOnlyList<PageDiagnostic> diagnostics) =>
        [.. diagnostics.Select(diagnostic => new {
            line = diagnostic.Position?.Line,
            column = diagnostic.Position?.Column,
            message = diagnostic.Message,
        })];
}

/// <summary>Checks the JavaScript in a complete page template supplied as content.</summary>
internal sealed class CheckScriptTextTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "check_script_text",
        "Check the JavaScript in a complete page template supplied as content, without storing "
        + "it. Parses the literal text as an ES module. Pass the whole page, not an edit_page "
        + "replacement fragment: a fragment can be invalid alone even when the edit is correct. "
        + "Checks syntax only; imports are not resolved and nothing is executed.",
        false,
        [new("content", ToolArgumentType.String, true)]) { Verb = "Check" };

    public override Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) =>
        Task.FromResult(CheckScriptTool.CheckTemplate(services, values.RequiredString("content"), path: null));
}
