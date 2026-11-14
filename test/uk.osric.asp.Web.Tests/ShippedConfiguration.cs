// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.Configuration;
using System.Reflection;

/// <summary>The settings the site ships with, read from the real <c>appsettings.json</c>.</summary>
/// <remarks>
/// The shipped file is the source of truth for every policy value, so a test starts from it and
/// overrides what it is about rather than writing a second copy of the values. Configuration
/// providers merge key by key, so an override names only the key it changes.
/// </remarks>
internal static class ShippedConfiguration {

    /// <summary>Reads the shipped settings.</summary>
    public static IConfiguration Read() => new ConfigurationBuilder().AddJsonFile(FilePath()).Build();

    /// <summary>Reads the shipped settings with some keys replaced or added.</summary>
    /// <param name="overrides">The keys to override, in configuration-key form.</param>
    public static IConfiguration Read(params (string Key, string? Value)[] overrides) {
        ConfigurationBuilder builder = new();
        _ = builder.AddJsonFile(FilePath());
        if (overrides.Length > 0) {
            _ = builder.AddInMemoryCollection(overrides.Select(overrideValue =>
                new KeyValuePair<string, string?>(overrideValue.Key, overrideValue.Value)));
        }

        return builder.Build();
    }

    /// <summary>Reads the shipped settings without one key, so a missing value can be exercised.</summary>
    /// <param name="key">The key to remove.</param>
    public static IConfiguration Without(string key) {
        Dictionary<string, string?> values = Values();
        _ = values.Remove(key);

        return Build(values);
    }

    /// <summary>Reads the shipped settings without a whole section, so a missing section can be exercised.</summary>
    /// <param name="section">The section to remove.</param>
    public static IConfiguration WithoutSection(string section) {
        Dictionary<string, string?> values = Values();
        foreach (string key in values.Keys.Where(key =>
                     key.Equals(section, StringComparison.OrdinalIgnoreCase)
                     || key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase)).ToList()) {
            _ = values.Remove(key);
        }

        return Build(values);
    }

    private static string FilePath() => Path.Combine(
        RepoRoot(),
        "src", "uk.osric.asp.Web", "appsettings.json");

    private static string RepoRoot() => typeof(ShippedConfiguration).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "RepoRoot")
        .Value!;

    private static Dictionary<string, string?> Values() =>
        Read().AsEnumerable().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
