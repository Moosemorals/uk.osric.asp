// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using uk.osric.asp.Web.Configuration;

/// <summary>Every bound an import archive may reach, in one place.</summary>
/// <remarks>
/// These bound one hostile upload; they are not a policy on the site's own pages. A large site's
/// export can exceed <see cref="MaxUploadBytes"/>, and raising the setting is the answer, not
/// relaxing the validation. Every value comes from <c>Pages:Archive</c>.
/// </remarks>
internal sealed record ArchiveOptions {

    /// <summary>The most bytes one uploaded archive may be.</summary>
    public required int MaxUploadBytes { get; init; }

    /// <summary>The most pages one archive may carry.</summary>
    public required int MaxPages { get; init; }

    /// <summary>The most compiled bytes one batch may hold before it commits.</summary>
    public required long MaxCompiledBytes { get; init; }

    /// <summary>How long an unapproved review survives.</summary>
    public required int StageTtlMinutes { get; init; }

    /// <summary>How many reviewed archives are held in memory.</summary>
    public required int MaxStagedImports { get; init; }

    /// <summary>The stage time-to-live as a time span.</summary>
    public TimeSpan StageTtl => TimeSpan.FromMinutes(StageTtlMinutes);

    /// <summary>Reads <c>Pages:Archive</c>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ConfigurationException">A value is missing or unusable.</exception>
    public static ArchiveOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        ConfigurationReader reader = new(configuration, "Pages:Archive");
        ArchiveOptions options = FromConfiguration(reader);
        reader.Complete();
        return options;
    }

    /// <summary>Reads <c>Pages:Archive</c> into the caller's report.</summary>
    /// <param name="reader">A reader opened on <c>Pages:Archive</c>, which the caller completes.</param>
    internal static ArchiveOptions FromConfiguration(ConfigurationReader reader) {
        ArgumentNullException.ThrowIfNull(reader);

        ArchiveOptions options = new() {
            MaxUploadBytes = reader.Number("MaxUploadBytes", recommended: 16 * 1024 * 1024,
                minimum: 1024, maximum: 512 * 1024 * 1024),
            MaxPages = reader.Number("MaxPages", recommended: 500, minimum: 1, maximum: 100_000),
            MaxCompiledBytes = reader.Number("MaxCompiledBytes", recommended: 128L * 1024 * 1024,
                minimum: 1024 * 1024, maximum: 2_147_483_648),
            StageTtlMinutes = reader.Number("StageTtlMinutes", recommended: 30, minimum: 1, maximum: 1_440),
            MaxStagedImports = reader.Number("MaxStagedImports", recommended: 3, minimum: 1, maximum: 32),
        };
        return options;
    }
}
