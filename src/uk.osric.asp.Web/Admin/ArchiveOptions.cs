// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

/// <summary>Every bound an import archive may reach, in one place.</summary>
/// <remarks>
/// These bound one hostile upload; they are not a policy on the site's own pages. A large site's
/// export can exceed <see cref="MaxUploadBytes"/>, and raising the setting is the answer, not
/// relaxing the validation.
/// </remarks>
internal sealed record ArchiveOptions {

    /// <summary>The default cap on one uploaded archive, in bytes: 16 MiB.</summary>
    public const int DefaultMaxUploadBytes = 16 * 1024 * 1024;

    /// <summary>The default cap on the pages one archive may carry.</summary>
    public const int DefaultMaxPages = 500;

    /// <summary>The default cap on the compiled images held until a batch commits: 128 MiB.</summary>
    public const long DefaultMaxCompiledBytes = 128L * 1024 * 1024;

    /// <summary>How long an unapproved review survives, when none is configured.</summary>
    public const int DefaultStageTtlMinutes = 30;

    /// <summary>How many reviewed archives are held in memory, when none is configured.</summary>
    public const int DefaultMaxStagedImports = 3;

    /// <summary>The most bytes one uploaded archive may be.</summary>
    public int MaxUploadBytes { get; init; } = DefaultMaxUploadBytes;

    /// <summary>The most pages one archive may carry.</summary>
    public int MaxPages { get; init; } = DefaultMaxPages;

    /// <summary>The most compiled bytes one batch may hold before it commits.</summary>
    public long MaxCompiledBytes { get; init; } = DefaultMaxCompiledBytes;

    /// <summary>How long an unapproved review survives.</summary>
    public int StageTtlMinutes { get; init; } = DefaultStageTtlMinutes;

    /// <summary>How many reviewed archives are held in memory.</summary>
    public int MaxStagedImports { get; init; } = DefaultMaxStagedImports;

    /// <summary>The stage time-to-live as a time span.</summary>
    public TimeSpan StageTtl => TimeSpan.FromMinutes(StageTtlMinutes);

    /// <summary>Reads <c>Pages:Archive</c>, clamping every value to one that still bounds the upload.</summary>
    /// <param name="configuration">The application configuration.</param>
    public static ArchiveOptions FromConfiguration(IConfiguration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Pages:Archive");

        return new ArchiveOptions {
            MaxUploadBytes = Math.Max(1024, section.GetValue("MaxUploadBytes", DefaultMaxUploadBytes)),
            MaxPages = Math.Max(1, section.GetValue("MaxPages", DefaultMaxPages)),
            MaxCompiledBytes = Math.Max(1024 * 1024, section.GetValue("MaxCompiledBytes", DefaultMaxCompiledBytes)),
            StageTtlMinutes = Math.Max(1, section.GetValue("StageTtlMinutes", DefaultStageTtlMinutes)),
            MaxStagedImports = Math.Max(1, section.GetValue("MaxStagedImports", DefaultMaxStagedImports)),
        };
    }
}
