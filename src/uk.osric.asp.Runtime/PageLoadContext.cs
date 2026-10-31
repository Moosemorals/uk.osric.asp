// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime;

using System.Reflection;
using System.Runtime.Loader;

/// <summary>The load context every compiled page gets.</summary>
/// <remarks>
/// <para>
/// A page may use the SDK and the base class library, but not the site: naming one of the shell's
/// own assemblies resolves here, and this context refuses it instead of falling back to the default
/// one. Throwing rather than returning <see langword="null"/> is the point - <see langword="null"/>
/// means "ask the default context", which is how a page could otherwise reach
/// <c>uk.osric.asp.Web</c> by name.
/// </para>
/// <para>
/// <c>uk.osric.asp.Core</c> is deliberately not denied: it is the page SDK, and the fallback to the
/// default context hands the page the same copy of <see cref="IContext"/> the host is using, so the
/// handler signature matches.
/// </para>
/// <para>
/// This is isolation from the app's internals, not a sandbox. A page still runs with the site's
/// identity and can use the base class library; only the shell and the web stack are out of reach.
/// </para>
/// </remarks>
internal sealed class PageLoadContext() : AssemblyLoadContext("pages", isCollectible: true) {

    private static readonly string[] s_deniedPrefixes = [
        "Microsoft.AspNetCore",
        "Microsoft.Data.Sqlite",
        "Microsoft.EntityFrameworkCore",
        "uk.osric.asp.Runtime",
        "uk.osric.asp.Web",
    ];

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">The page tried to load an assembly it may not use.</exception>
    protected override Assembly? Load(AssemblyName assemblyName) {
        ArgumentNullException.ThrowIfNull(assemblyName);

        string? name = assemblyName.Name;

        if (name is not null) {
            foreach (string prefix in s_deniedPrefixes) {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                    // Not a FileNotFoundException: the runtime treats that as "not here", carries on
                    // to the default context and resolves the assembly anyway.
                    throw new NotSupportedException($"A page may not load {name}.");
                }
            }
        }

        return null;
    }
}
