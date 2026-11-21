// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using uk.osric.asp.Runtime;
using uk.osric.asp.Testing;

/// <summary>The real compiler the fixtures share, wrapped so no source is compiled twice a process.</summary>
internal static class TestCompiler {

    /// <summary>The one real compiler every fixture binds against.</summary>
    public static IPageCompiler Real { get; } = new CachingCompiler(new PageCompiler());

    /// <summary>A counting compiler over <see cref="Real"/>, which a fixture keeps for its own tests.</summary>
    /// <returns>The counter the fixture asserts against.</returns>
    public static RecordingCompiler Counting() => new(Real);
}
