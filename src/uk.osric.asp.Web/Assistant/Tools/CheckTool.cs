// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;
using uk.osric.asp.Web.Pages;
using SdkDiagnostic = uk.osric.asp.Sdk.PageDiagnostic;

/// <summary>Checks a stored page or supplied source without storing anything.</summary>
internal sealed class CheckTool : AssistantTool {

    public override ToolSpec Spec { get; } = new(
        "check",
        "Check a template, or the script it serves, without storing it. `language` names what to check: "
        + "`template` compiles the page as a save would — generation, the page API policy and the compiler, "
        + "against the libraries the page's usings reach — and `javascript` blanks the template constructs and "
        + "parses the literal text as an ES module. Give exactly one of `path` and `content`: `path` checks a "
        + "stored page's source, and a shell file has none and is refused; `content` checks a complete source "
        + "you supply, which must be the whole template rather than an edit_page fragment, because a fragment "
        + "can be invalid alone even when the edit is correct. Nothing is stored, nothing runs and imports are "
        + "not resolved. The answer carries `valid` and one entry per check with `language`, `kind` (the page "
        + "kind generation found, null for a script), `valid`, `diagnostics` and whether that list was cut.",
        false,
        [
            new("language", ToolArgumentType.String, true) {
                Values = PageCheckLanguages.Names,
                Description = "What to check: a value from the closed set this build answers for.",
            },
            new("path", ToolArgumentType.String, false) {
                Description = "A stored page's key, checked as it stands. Give this or `content`, never both.",
            },
            new("content", ToolArgumentType.String, false) {
                Description = "A complete source to check, not a fragment. Give this or `path`, never both.",
            },
        ]) { Verb = "Check" };

    public override async Task<ToolExecutionResult> RunAsync(
        ToolValues values,
        ToolServices services,
        CancellationToken cancellationToken) {
        string languageName = values.RequiredString("language");
        if (!PageCheckLanguages.TryParse(languageName, out PageCheckLanguage language)) {
            return services.Results.Failure(
                $"`language` must be one of {string.Join(", ", PageCheckLanguages.Names)}.",
                $"{Spec.Verb} refused");
        }

        string? pathText = values.String("path");
        string? content = values.String("content");
        if (pathText is null == content is null) {
            return services.Results.Failure(
                "Give exactly one of `path` and `content`.", $"{Spec.Verb} refused");
        }

        PagePath? path = null;
        string source;
        if (pathText is not null) {
            (PagePath? resolved, ToolExecutionResult? refusal) = ToolPaths.ResolveRead(Spec, values, "path", services);
            if (refusal is not null) {
                return refusal;
            }

            path = resolved!;
            if (ToolPaths.RequirePageSource(Spec, path, "checks page source", services) is { } noSource) {
                return noSource;
            }

            PageRead? page = await services.Store.ReadAsync(path, cancellationToken);
            if (page is null) {
                return services.Results.Failure(
                    $"Page {path.Describe()} was not found.", $"{Spec.Verb} {path.Describe()}: not found");
            }

            source = page.Text;
        } else {
            source = content!;
        }

        // The tool's own deadline bounds the wait and the compile, so a check that cannot get a slot
        // answers PAGE010 rather than holding the call open.
        PageCheckScope check = services.Checks.For(path);
        check.AttachDeadline(cancellationToken);

        PageCheckResult result = await check.CheckAsync(languageName, source);

        // Roslyn cannot stop at a diagnostic count, so a check always produces what it produces: the
        // answer carries the first few and says the list was cut, rather than refusing the call.
        IReadOnlyList<SdkDiagnostic> shown = [.. result.Diagnostics.Take(services.Options.MaxCheckDiagnostics)];

        object data = new {
            valid = result.Valid,
            checks = new[] {
                new {
                    language = PageCheckLanguages.NameOf(language),
                    kind = result.Kind?.ToString(),
                    valid = result.Valid,
                    diagnostics = shown.Select(Diagnostic),
                    truncated = shown.Count < result.Diagnostics.Count,
                },
            },
        };

        string where = path is null ? "supplied text" : path.Describe();
        string summary = result.Diagnostics.Count == 0
            ? $"Checked {where}: valid"
            : $"Checked {where}: {result.Diagnostics.Count} problem{(result.Diagnostics.Count == 1 ? string.Empty : "s")}";

        return services.Results.Success(data, summary);
    }

    private static object Diagnostic(SdkDiagnostic diagnostic) => new {
        code = diagnostic.Code,
        severity = diagnostic.Severity.ToString(),
        source = diagnostic.Source,
        line = diagnostic.Line,
        column = diagnostic.Column,
        endLine = diagnostic.EndLine,
        endColumn = diagnostic.EndColumn,
        message = diagnostic.Message,
    };
}
