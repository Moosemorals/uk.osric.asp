// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Admin;

using uk.osric.asp.Sdk;

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
/// A missing file is not an error, and neither is an unreadable one: a build that did not come through
/// the Makefile carries none, and a file the process cannot open is left out with a warning. Each file
/// is read lazily and kept.
/// </para>
/// </remarks>
internal sealed class LicenceLibrary {

    private readonly string _directory;
    private readonly ILogger<LicenceLibrary> _logger;
    private readonly Lazy<Library> _library;

    /// <summary>Reads the files beside a directory.</summary>
    /// <param name="directory">The directory the build copied them into, normally the assembly's own.</param>
    /// <param name="logger">Where a file that cannot be read is reported.</param>
    public LicenceLibrary(string directory, ILogger<LicenceLibrary> logger) {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(logger);

        _directory = directory;
        _logger = logger;
        _library = new Lazy<Library>(Read, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The site's own licence text (<c>LICENSE</c>), or null when the build does not carry it.</summary>
    public string? SiteLicence => _library.Value.SiteLicence;

    /// <summary>The notice the image carries for the packages it ships, or null.</summary>
    public string? ThirdPartyNotices => _library.Value.ThirdPartyNotices;

    /// <summary>The REUSE annotation file, or null.</summary>
    public string? Reuse => _library.Value.Reuse;

    /// <summary>The licence texts in <c>LICENSES</c>, in name order; empty when the folder is not carried.</summary>
    public IReadOnlyList<LicenceText> Licences => _library.Value.Licences;

    /// <summary>The files as a page reads them, in a fixed order, absent ones left out.</summary>
    public IReadOnlyList<SiteDocument> Documents => _library.Value.Documents;

    private Library Read() {
        string? licence = ReadText("LICENSE");
        string? notices = ReadText("THIRD-PARTY-NOTICES.md");
        string? reuse = ReadText("REUSE.toml");
        IReadOnlyList<LicenceText> licences = ReadLicences();

        List<SiteDocument> documents = [];
        if (licence is not null) {
            documents.Add(new SiteDocument(SiteDocumentKind.SiteLicence, "LICENSE", SiteDocumentFormat.Text, licence));
        }

        if (notices is not null) {
            documents.Add(new SiteDocument(
                SiteDocumentKind.ThirdPartyNotices, "THIRD-PARTY-NOTICES.md", SiteDocumentFormat.Markdown, notices));
        }

        foreach (LicenceText text in licences) {
            documents.Add(new SiteDocument(SiteDocumentKind.LicenceText, text.Name, SiteDocumentFormat.Text, text.Text));
        }

        if (reuse is not null) {
            documents.Add(new SiteDocument(SiteDocumentKind.ReuseAnnotation, "REUSE.toml", SiteDocumentFormat.Text, reuse));
        }

        return new Library(licence, notices, reuse, licences, documents);
    }

    private string? ReadText(string name) {
        string path = Path.Combine(_directory, name);
        if (!File.Exists(path)) {
            return null;
        }

        try {
            return File.ReadAllText(path);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            _logger.LogWarning(exception, "Could not read {File}; the copyright page will leave it out", name);

            return null;
        }
    }

    private IReadOnlyList<LicenceText> ReadLicences() {
        string folder = Path.Combine(_directory, "LICENSES");
        if (!Directory.Exists(folder)) {
            return [];
        }

        string[] files;
        try {
            files = Directory.GetFiles(folder, "*.txt", SearchOption.TopDirectoryOnly);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            _logger.LogWarning(exception, "Could not read the LICENSES folder; the copyright page will leave it out");

            return [];
        }

        Array.Sort(files, StringComparer.Ordinal);

        List<LicenceText> texts = [];
        foreach (string file in files) {
            try {
                texts.Add(new LicenceText(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)));
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                _logger.LogWarning(
                    exception,
                    "Could not read {File}; the copyright page will leave it out",
                    Path.GetFileName(file));
            }
        }

        return texts;
    }

    private sealed record Library(
        string? SiteLicence,
        string? ThirdPartyNotices,
        string? Reuse,
        IReadOnlyList<LicenceText> Licences,
        IReadOnlyList<SiteDocument> Documents);
}
