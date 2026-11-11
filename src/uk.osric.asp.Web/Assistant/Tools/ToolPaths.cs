// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant.Tools;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Core.Text;

/// <summary>The gate every path-naming tool repeats, in the one order it must happen.</summary>
/// <remarks>
/// A write is validated against the policy and nothing more: the policy already refuses a reserved
/// or protected key for a write. A read is validated the same way and then asked whether a reserved
/// route shadows the key, because the policy permits a read beneath a reserved route and the tool
/// must not. The refusal's summary uses the tool's own verb, so its wording has one source.
/// </remarks>
internal static class ToolPaths {

    /// <summary>The path a read names, or the refusal that stops the call.</summary>
    /// <param name="spec">The tool, for its verb.</param>
    /// <param name="values">The validated arguments.</param>
    /// <param name="argument">The argument that carries the path.</param>
    /// <param name="services">The policy and where the refusal is written.</param>
    public static (PagePath? Path, ToolExecutionResult? Refusal) ResolveRead(
        ToolSpec spec,
        ToolValues values,
        string argument,
        ToolServices services) {
        PathValidationResult checkedPath = services.Paths.Validate(values.String(argument), write: false);
        if (!checkedPath.IsSuccess) {
            return (null, services.Results.Failure(checkedPath.Error!, $"{spec.Verb} refused"));
        }

        PagePath path = checkedPath.Path!;
        if (services.Paths.ShadowedBy(path) is { } owner
            && !string.Equals(owner.Key, path.Key, StringComparison.Ordinal)) {
            return (null, services.Results.Failure(
                $"The key {Describe(path)} is beneath the shell file {Describe(owner)}. Read {Describe(owner)} instead.",
                $"Read {Describe(path)}: beneath {Describe(owner)}"));
        }

        return (path, null);
    }

    /// <summary>The path a write names, or the refusal that stops the call.</summary>
    /// <param name="spec">The tool, for its verb.</param>
    /// <param name="values">The validated arguments.</param>
    /// <param name="argument">The argument that carries the path.</param>
    /// <param name="services">The policy and where the refusal is written.</param>
    public static (PagePath? Path, ToolExecutionResult? Refusal) ResolveWrite(
        ToolSpec spec,
        ToolValues values,
        string argument,
        ToolServices services) {
        PathValidationResult checkedPath = services.Paths.Validate(values.String(argument), write: true);
        return checkedPath.IsSuccess
            ? (checkedPath.Path!, null)
            : (null, services.Results.Failure(checkedPath.Error!, $"{spec.Verb} refused"));
    }

    /// <summary>The refusal a tool that needs page source gives a shell file, or null.</summary>
    /// <param name="spec">The tool, for its name and verb.</param>
    /// <param name="path">The path the tool was asked to read.</param>
    /// <param name="reads">What the tool reads, as a clause: "reads page source".</param>
    /// <param name="services">The policy and where the refusal is written.</param>
    public static ToolExecutionResult? RequirePageSource(
        ToolSpec spec,
        PagePath path,
        string reads,
        ToolServices services) =>
        services.Paths.IsReserved(path)
            ? services.Results.Failure(
                $"{spec.Name} {reads}, and {Describe(path)} is a shell file with no page source. read_page describes it instead.",
                $"{spec.Verb} refused")
            : null;

    /// <summary>A path as a refusal may repeat it.</summary>
    /// <param name="path">The path to describe.</param>
    public static string Describe(PagePath path) => MessageText.Sanitize(path.Key);
}
