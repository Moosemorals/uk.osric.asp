// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

using System.Collections.Frozen;

/// <summary>What page code is allowed to name.</summary>
/// <remarks>
/// <para>
/// Default deny: a type is usable only because it appears below, so a new capability in the base
/// class library is not granted to page code by accident. The list is data rather than a filter
/// chain so a rejection is one readable line away from being fixed, and the diagnostic names the
/// exact API to add.
/// </para>
/// <para>
/// Deliberately absent, and why: <c>System.Object</c> (its <c>GetType</c> would hand a page
/// reflection), <c>System.Type</c>, <c>System.IO</c>, <c>System.Net</c>, <c>System.Diagnostics</c>,
/// <c>System.Reflection</c>, <c>System.Linq.Expressions</c>, <c>System.Threading</c> beyond the
/// cancellation token, <c>System.Runtime.InteropServices</c>, <c>System.Runtime.Loader</c>,
/// <c>System.Runtime.CompilerServices</c>, <c>System.Text.RegularExpressions</c>, and anything in
/// <c>Microsoft.*</c> or our own shell assemblies. <c>System.Net</c> stays out on purpose:
/// <c>IHttpFetch</c> is the page's only egress.
/// </para>
/// <para>
/// <c>System.Threading.Tasks</c> is admitted as a type rather than as a namespace: exactly
/// <see cref="Task"/> is allowed, so a page can write and await a task, and
/// <c>TaskCompletionSource</c>, <c>TaskScheduler</c>, <c>Parallel</c> and <c>ValueTask</c> stay
/// denied without being named. Ten of <c>Task</c>'s own members are denied by name in
/// <c>s_deniedApis</c>, because each starts work the render deadline cannot stop or turns an
/// <c>await</c> back into a blocked thread.
/// </para>
/// </remarks>
public static class PageApiPolicy {

    private static readonly FrozenSet<string> s_allowedNamespaces = new[] {
        "System.Collections.Generic",
        "System.Globalization",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> s_allowedTypes = new[] {
        "System.Action",
        "System.Array",
        "System.Boolean",
        "System.Byte",
        "System.Char",
        "System.Convert",
        "System.DateTime",
        "System.DateTimeKind",
        "System.DateTimeOffset",
        "System.Decimal",
        "System.Double",
        "System.Enum",
        "System.Func",
        "System.Guid",
        "System.Int16",
        "System.Int32",
        "System.Int64",
        "System.Linq.Enumerable",
        "System.Math",
        "System.Nullable",
        "System.Predicate",
        "System.SByte",
        "System.Single",
        "System.String",
        "System.StringComparison",
        "System.StringSplitOptions",
        "System.Text.Json.JsonDocument",
        "System.Text.Json.JsonElement",
        "System.Text.Json.JsonException",
        "System.Text.Json.JsonSerializer",
        "System.Text.Json.JsonSerializerDefaults",
        "System.Text.Json.JsonSerializerOptions",
        "System.Text.Json.JsonValueKind",
        "System.Text.StringBuilder",
        "System.Threading.CancellationToken",
        "System.Threading.Tasks.Task",
        "System.TimeSpan",
        "System.UInt16",
        "System.UInt32",
        "System.UInt64",
        "System.Uri",
    }.ToFrozenSet(StringComparer.Ordinal);

    // Admitted by type, denied by member: Task is the page's shape for waiting, not a way to start
    // work beside the render or to turn an await back into a blocked thread.
    private static readonly FrozenSet<string> s_deniedApis = new[] {
        "System.Threading.Tasks.Task.Run",
        "System.Threading.Tasks.Task.Factory",
        "System.Threading.Tasks.Task.ContinueWith",
        "System.Threading.Tasks.Task.Wait",
        "System.Threading.Tasks.Task.WaitAll",
        "System.Threading.Tasks.Task.WaitAny",
        "System.Threading.Tasks.Task.Result",
        "System.Threading.Tasks.Task.GetAwaiter",
        "System.Threading.Tasks.Task.Delay",
        "System.Threading.Tasks.Task.Yield",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Reports every reference the policy does not allow.</summary>
    /// <param name="used">The APIs the page referenced, in the order they appear.</param>
    /// <returns>
    /// One diagnostic per distinct API, naming the API and its template position; empty when the
    /// page is within the policy.
    /// </returns>
    public static IReadOnlyList<PageDiagnostic> Check(IEnumerable<ApiReference> used) {
        ArgumentNullException.ThrowIfNull(used);

        List<PageDiagnostic> diagnostics = [];
        HashSet<string> reported = new(StringComparer.Ordinal);

        foreach (ApiReference reference in used) {
            if (IsAllowed(reference)) {
                continue;
            }

            string name = Describe(reference);
            if (!reported.Add(name)) {
                continue;
            }

            diagnostics.Add(new PageDiagnostic(
                PageDiagnosticSeverity.Error,
                reference.Position,
                Message(name)));
        }

        return diagnostics;
    }

    private static string Describe(ApiReference reference) {
        return string.IsNullOrEmpty(reference.Member)
            ? $"{reference.Namespace}.{reference.Type}"
            : $"{reference.Namespace}.{reference.Type}.{reference.Member}";
    }

    private static string Message(string name) {
        // Two reasons, one sentence, because both come to the same thing: the render's deadline is the
        // site's only bound, and every denied member is a way round it.
        return s_deniedApis.Contains(name)
            ? $"PAGE001: pages may not use {name}; it starts or blocks work the render cannot stop."
            : $"PAGE001: pages may not use {name}.";
    }

    private static bool IsAllowed(ApiReference reference) {
        // Member-level denial comes first: Task is admitted as a type, so the type check below would
        // otherwise let its whole surface through.
        if (s_deniedApis.Contains(Describe(reference))) {
            return false;
        }

        if (string.Equals(reference.Namespace, TemplateSourceGenerator.SdkNamespace, StringComparison.Ordinal)) {
            return true;
        }

        // An exception type carries no capability, and a page that catches one has to name it;
        // matching the family avoids a list that grows with every try/catch anyone writes.
        if (string.Equals(reference.Namespace, "System", StringComparison.Ordinal)
            && reference.Type.EndsWith("Exception", StringComparison.Ordinal)) {
            return true;
        }

        return s_allowedNamespaces.Contains(reference.Namespace)
            || s_allowedTypes.Contains($"{reference.Namespace}.{reference.Type}");
    }
}
