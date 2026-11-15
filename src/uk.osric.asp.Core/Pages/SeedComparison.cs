// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Pages;

/// <summary>What a page's stored content is, relative to the seed the release ships.</summary>
public enum SeedVerdict {
    /// <summary>The page holds exactly the seed this release ships.</summary>
    InSync,

    /// <summary>The release moved the seed, and the page still holds the one it was installed from.</summary>
    ReleaseNewer,

    /// <summary>The page changed after its seed set it, and the seed itself did not move.</summary>
    Edited,

    /// <summary>The page changed after its seed set it, and the release moved the seed too.</summary>
    Conflict,

    /// <summary>The page holds the shipped bytes but is not recorded as installed from them.</summary>
    StoreNewer,

    /// <summary>The page has no record of coming from a seed.</summary>
    Unknown,

    /// <summary>The release no longer ships a page at this key.</summary>
    Retired,
}

/// <summary>The three-value comparison, and the two writes it permits.</summary>
/// <param name="Verdict">What the row is, relative to the seed.</param>
/// <param name="CanApply">
/// Whether an automatic write may replace the page with the shipped seed: the page still holds the
/// seed it was installed from, and the release has moved the seed on.
/// </param>
/// <param name="CanAdopt">
/// Whether the store may record the shipped seed as installed without writing a revision: the page
/// already holds exactly those bytes, and the record says otherwise.
/// </param>
/// <remarks>
/// <para>
/// This is the whole seed rule: <strong>an automatic write requires H = R and S ≠ R</strong>, where
/// R is the seed hash recorded on the page, S is the seed this release ships and H is the content
/// hash of the page's stored source. Everything else is a verdict that says why not.
/// </para>
/// <para>
/// The two row states — a key with no head row, and a tombstone — are not comparisons and are not
/// decided here: there is nothing to compare, and only the caller knows whether the key is shipped.
/// </para>
/// </remarks>
public readonly record struct SeedComparison(SeedVerdict Verdict, bool CanApply, bool CanAdopt) {

    /// <summary>Compares a page's recorded anchor, the shipped seed and the live content.</summary>
    /// <param name="recorded">R, or <see langword="null"/> when the page has no record of a seed.</param>
    /// <param name="shipped">S, or <see langword="null"/> when the release ships no seed for the key.</param>
    /// <param name="content">H, or <see langword="null"/> when the page has no live content.</param>
    /// <returns>The verdict, and whether the row may be applied or adopted.</returns>
    public static SeedComparison Of(string? recorded, string? shipped, string? content) {
        bool canAdopt = shipped is not null
            && string.Equals(content, shipped, StringComparison.Ordinal)
            && !string.Equals(recorded, shipped, StringComparison.Ordinal);

        if (shipped is null) {
            return new SeedComparison(SeedVerdict.Retired, CanApply: false, CanAdopt: false);
        }

        if (recorded is null) {
            return new SeedComparison(SeedVerdict.Unknown, CanApply: false, canAdopt);
        }

        bool anchored = string.Equals(content, recorded, StringComparison.Ordinal);
        if (string.Equals(recorded, shipped, StringComparison.Ordinal)) {
            return anchored
                ? new SeedComparison(SeedVerdict.InSync, CanApply: false, CanAdopt: false)
                : new SeedComparison(SeedVerdict.Edited, CanApply: false, CanAdopt: false);
        }

        if (anchored) {
            return new SeedComparison(SeedVerdict.ReleaseNewer, CanApply: true, CanAdopt: false);
        }

        return string.Equals(content, shipped, StringComparison.Ordinal)
            ? new SeedComparison(SeedVerdict.StoreNewer, CanApply: false, CanAdopt: true)
            : new SeedComparison(SeedVerdict.Conflict, CanApply: false, CanAdopt: false);
    }
}
