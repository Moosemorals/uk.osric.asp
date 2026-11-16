// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Pages;

using uk.osric.asp.Core.Pages;
using uk.osric.asp.Sdk;

/// <summary>The words a seed verdict is reported in, beside <see cref="PageImageStatus"/>.</summary>
/// <remarks>
/// The comparison is the core's; the sentence is the host's, because it is the host that knows what it
/// will and will not write. A page renders these words and offers the actions the flags permit, so the
/// listing, the terminal and the admin views cannot describe one row three ways.
/// </remarks>
internal static class SeedVerdictWords {

    /// <summary>A live page's summary, from its comparison.</summary>
    /// <param name="comparison">The comparison of R, S and H.</param>
    /// <param name="shipped">The hash this release ships, or null when it ships none.</param>
    public static PageSeedSummary Describe(this SeedComparison comparison, string? shipped) {
        return new PageSeedSummary(Label(comparison.Verdict), Sentence(comparison, shipped), comparison.CanApply, comparison.CanAdopt);
    }

    /// <summary>A key this release ships that the store does not have.</summary>
    public static PageSeedSummary New() =>
        new("new", "This release ships a page here that the store does not have. Installing creates it.", CanApply: true, CanAdopt: false);

    /// <summary>A tombstone at a key this release still ships.</summary>
    public static PageSeedSummary Deleted() =>
        new("deleted", "Deleted here, and still shipped by this release. Restoring writes the bundled source as a new revision; it is not offered as an install.", CanApply: false, CanAdopt: false);

    private static string Sentence(SeedComparison comparison, string? shipped) {
        string shortHash = shipped is null ? string.Empty : shipped[..4];

        return comparison.Verdict switch {
            SeedVerdict.InSync => "Holds the seed this release ships.",
            SeedVerdict.ReleaseNewer =>
                $"Release {shortHash}… updated this page, and you have not changed it since it was installed. Applying saves the new seed; the diff shows what changes.",
            SeedVerdict.Edited =>
                "Changed since the seed last set it; this release did not move the seed. The diff shows both.",
            SeedVerdict.Conflict =>
                "Changed since the seed last set it, and this release changed the seed too. Compare, then choose.",
            SeedVerdict.StoreNewer =>
                "Holds the bytes this release ships, but is not recorded as installed from it. Recording it as installed makes it a seeded page and keeps this content.",
            SeedVerdict.Retired =>
                "This release no longer ships this page. It is left alone, and it is an ordinary live page.",
            _ => comparison.CanAdopt
                ? "No record of this page coming from a seed. Its content already matches what this release ships, so applying would change nothing; recording it as installed makes it a seeded page."
                : "No record of this page coming from a seed. It is not updated automatically.",
        };
    }

    private static string Label(SeedVerdict verdict) => verdict switch {
        SeedVerdict.InSync => "in sync",
        SeedVerdict.ReleaseNewer => "release newer",
        SeedVerdict.Edited => "edited",
        SeedVerdict.Conflict => "conflict",
        SeedVerdict.StoreNewer => "store newer",
        SeedVerdict.Retired => "retired",
        _ => "unknown",
    };
}
