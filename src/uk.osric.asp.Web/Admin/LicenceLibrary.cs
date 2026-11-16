// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

/// <summary>One licence text the build carries.</summary>
/// <param name="Name">The licence's name, from its file name: <c>MIT</c>, <c>ISC</c>, <c>BSD-3-Clause</c>.</param>
/// <param name="Text">The text exactly as the file holds it.</param>
internal sealed record LicenceText(string Name, string Text);

/// <summary>The copyright and licence files this build carries, read once.</summary>
/// <remarks>
/// <para>
/// The deployed image has no checkout, so the files are packed beside the assembly the way the
/// changelist and the third-party notices are. Reading them is the whole point: a page that copied
/// their text would be a second copy to keep in step, which is the drift this replaced.
/// </para>
/// <para>
/// A missing file is not an error. A build that did not come through the Makefile carries none, and
/// the page names what it lacks rather than failing; each file is read lazily and kept.
/// </para>
/// </remarks>
internal sealed class LicenceLibrary {

    private readonly string _directory;
    private readonly Lazy<IReadOnlyList<LicenceText>> _licences;

    /// <summary>Reads the files beside a directory.</summary>
    /// <param name="directory">The directory the build copied them into, normally the assembly's own.</param>
    public LicenceLibrary(string directory) {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;
        _licences = new Lazy<IReadOnlyList<LicenceText>>(ReadLicences);
    }

    /// <summary>The site's own licence text (<c>LICENSE</c>), or null when the build does not carry it.</summary>
    public string? SiteLicence => Read("LICENSE");

    /// <summary>The notice the image carries for the packages it ships, or null.</summary>
    public string? ThirdPartyNotices => Read("THIRD-PARTY-NOTICES.md");

    /// <summary>The REUSE annotation file, or null.</summary>
    public string? Reuse => Read("REUSE.toml");

    /// <summary>The licence texts in <c>LICENSES</c>, in name order; empty when the folder is not carried.</summary>
    public IReadOnlyList<LicenceText> Licences => _licences.Value;

    private string? Read(string name) {
        string path = Path.Combine(_directory, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private IReadOnlyList<LicenceText> ReadLicences() {
        string folder = Path.Combine(_directory, "LICENSES");
        if (!Directory.Exists(folder)) {
            return [];
        }

        string[] files = Directory.GetFiles(folder, "*.txt", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);

        return [.. files.Select(file => new LicenceText(
            Path.GetFileNameWithoutExtension(file),
            File.ReadAllText(file)))];
    }
}
