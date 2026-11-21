// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Web.Documentation;

/// <summary>The real documentation corpus, read once for every fixture that asserts against it.</summary>
/// <remarks>
/// The host reads the shipped XML files lazily and holds the result for the process, and building it
/// - parse every file, resolve <c>inheritdoc</c>, annotate every cref - costs a couple of seconds. A
/// fixture that built its own copy per test spent that on each one; the documents are immutable once
/// loaded, so the fixtures share the host's answer instead.
/// </remarks>
internal static class TestReference {

    private static readonly Lazy<ReferenceLibrary> s_library =
        new(() => new ReferenceLibrary(AppContext.BaseDirectory, NullLogger<ReferenceLibrary>.Instance),
            LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The corpus this build carries, read on first use.</summary>
    public static ReferenceLibrary Library => s_library.Value;
}
