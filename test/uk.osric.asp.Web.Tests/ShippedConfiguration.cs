// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using System.Reflection;

/// <summary>The settings the site ships with, read from the real <c>appsettings.json</c>.</summary>
/// <remarks>
/// A cap that matters to the running site is the one in the shipped configuration, which an option's
/// class default does not have to agree with.
/// </remarks>
internal static class ShippedConfiguration {

    /// <summary>Reads the shipped settings.</summary>
    public static IConfiguration Read() => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(
            typeof(ShippedConfiguration).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "RepoRoot")
                .Value!,
            "src", "uk.osric.asp.Web", "appsettings.json"))
        .Build();
}
