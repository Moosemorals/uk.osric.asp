// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

/// <summary>Transforms one normalised document into the fragment a shell page writes.</summary>
/// <remarks>
/// The stylesheet is embedded rather than seeded: it is the shell's presentation, not owner-editable
/// content, and it cannot drift from the attributes the normaliser writes. It is loaded once with
/// <c>XsltSettings.Default</c> and a null resolver, so script blocks and <c>document()</c> are off.
/// </remarks>
internal static class ReferenceRenderer {

    private const string ResourceName = "uk.osric.asp.Web.Documentation.reference.xslt";

    private static readonly Lazy<XslCompiledTransform> s_transform = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The fragment for one assembly.</summary>
    /// <param name="document">The normalised document.</param>
    public static string Render(XDocument document) {
        ArgumentNullException.ThrowIfNull(document);

        using StringWriter writer = new();
        s_transform.Value.Transform(document.CreateReader(), null, writer);
        return writer.ToString();
    }

    private static XslCompiledTransform Load() {
        using Stream stream = typeof(ReferenceRenderer).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded stylesheet {ResourceName} is missing.");
        using XmlReader reader = XmlReader.Create(
            stream,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

        XslCompiledTransform transform = new();
        transform.Load(reader, XsltSettings.Default, null);
        return transform;
    }
}
