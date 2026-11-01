// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core;

using System.Collections.Frozen;

/// <summary>One API a page referenced, as plain data.</summary>
/// <remarks>
/// The runtime projects Roslyn symbols onto these values, so the policy itself stays a pure function
/// over strings and never sees a compiler type.
/// </remarks>
/// <param name="Namespace">The namespace the API lives in, for example <c>System.IO</c>.</param>
/// <param name="Type">The type name without its namespace, for example <c>File</c>.</param>
/// <param name="Member">The member used, or <see cref="string.Empty"/> for a plain type reference.</param>
/// <param name="Position">Where the reference appears in template space, or null when it is generated code.</param>
public readonly record struct ApiReference(string Namespace, string Type, string Member, TemplatePosition? Position);

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
/// <c>Microsoft.*</c> or our own shell assemblies.
/// </para>
/// </remarks>
public static class PageApiPolicy {

    private const string SdkNamespace = "uk.osric.asp.Core";

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
        "System.Text.StringBuilder",
        "System.Threading.CancellationToken",
        "System.TimeSpan",
        "System.UInt16",
        "System.UInt32",
        "System.UInt64",
        "System.Uri",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The namespaces in which every type is allowed.</summary>
    internal static IReadOnlySet<string> AllowedNamespaces => s_allowedNamespaces;

    /// <summary>The individual types that are allowed.</summary>
    internal static IReadOnlySet<string> AllowedTypes => s_allowedTypes;

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
                $"PAGE001: pages may not use {name}."));
        }

        return diagnostics;
    }

    private static string Describe(ApiReference reference) {
        return string.IsNullOrEmpty(reference.Member)
            ? $"{reference.Namespace}.{reference.Type}"
            : $"{reference.Namespace}.{reference.Type}.{reference.Member}";
    }

    private static bool IsAllowed(ApiReference reference) {
        if (string.Equals(reference.Namespace, SdkNamespace, StringComparison.Ordinal)) {
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
