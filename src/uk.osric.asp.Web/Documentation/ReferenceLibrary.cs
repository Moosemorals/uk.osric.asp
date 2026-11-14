// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Xml;
using System.Xml.Linq;

/// <summary>One assembly's documentation as the page serves it.</summary>
/// <param name="Info">The assembly it describes.</param>
/// <param name="TypeCount">How many type sections it holds.</param>
/// <param name="EntryCount">How many documented entries the file carries.</param>
/// <param name="Document">The normalised tree the tool reads.</param>
/// <param name="Html">Its fragment, transformed on first use and kept for the process.</param>
internal sealed record ReferenceAssembly(
    ReferenceAssemblyInfo Info,
    int TypeCount,
    int EntryCount,
    XDocument Document,
    Lazy<string> Html);

/// <summary>A type the tool found, with the document it belongs to.</summary>
/// <param name="Info">The assembly.</param>
/// <param name="Type">Its <c>type</c> element.</param>
internal sealed record ReferenceTypeMatch(ReferenceAssemblyInfo Info, XElement Type);

/// <summary>What a type lookup found: one type, several, one elsewhere, or nothing.</summary>
/// <param name="Match">The one type in the page contract, when the name is unambiguous.</param>
/// <param name="Candidates">The matches when the name is ambiguous.</param>
/// <param name="Elsewhere">A type of that name in another carried assembly.</param>
internal sealed record ReferenceTypeLookup(
    ReferenceTypeMatch? Match,
    IReadOnlyList<ReferenceTypeMatch> Candidates,
    ReferenceTypeMatch? Elsewhere);

/// <summary>One line of a reference search.</summary>
/// <param name="Type">The type that holds the match.</param>
/// <param name="Member">The member, or null when the type itself matched.</param>
/// <param name="Kind">The word for the match's kind.</param>
/// <param name="Summary">Its summary text, flattened.</param>
internal sealed record ReferenceSearchMatch(string Type, string? Member, string Kind, string Summary);

/// <summary>The documentation this build carries, read once and held for the process.</summary>
/// <remarks>
/// The files cannot change while the image runs, so one read is enough. Loading is lazy: a host that
/// never answers <c>/admin/reference</c> and never calls the tool pays nothing. Every failure is a
/// missing assembly or a missing copy, never a request that fails.
/// </remarks>
internal sealed class ReferenceLibrary {

    /// <summary>The slug the assistant tool reads.</summary>
    public const string ContractSlug = "sdk";

    private readonly string _directory;
    private readonly ILogger<ReferenceLibrary> _logger;
    private readonly Lazy<Corpus> _corpus;

    /// <summary>Reads the files in a directory on first use.</summary>
    /// <param name="directory">Where the assemblies' XML files sit.</param>
    /// <param name="logger">Where a missing or malformed file is reported.</param>
    public ReferenceLibrary(string directory, ILogger<ReferenceLibrary> logger) {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(logger);

        _directory = directory;
        _logger = logger;
        _corpus = new Lazy<Corpus>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The assemblies this build carries, in reading order.</summary>
    public IReadOnlyList<ReferenceAssembly> Assemblies => _corpus.Value.Assemblies;

    /// <summary>One assembly by its URL segment, or null when this build does not carry it.</summary>
    /// <param name="slug">The segment under <c>/admin/reference</c>.</param>
    public ReferenceAssembly? Find(string slug) =>
        Assemblies.FirstOrDefault(assembly => string.Equals(assembly.Info.Slug, slug, StringComparison.Ordinal));

    /// <summary>One assembly's fragment, transformed on first use.</summary>
    /// <param name="slug">The segment under <c>/admin/reference</c>.</param>
    public string? Render(string slug) => Find(slug)?.Html.Value;

    /// <summary>A type by local name, relative name, generic form or raw id.</summary>
    /// <param name="name">What the model named.</param>
    public ReferenceTypeLookup FindType(string name) {
        ArgumentNullException.ThrowIfNull(name);

        string wanted = NormalizeTypeQuery(name);
        List<ReferenceTypeMatch> contract = [];
        List<ReferenceTypeMatch> elsewhere = [];

        foreach (ReferenceAssembly assembly in Assemblies) {
            foreach (XElement type in assembly.Document.Descendants("type")) {
                if (TypeMatches(type, wanted)) {
                    ReferenceTypeMatch match = new(assembly.Info, type);
                    if (string.Equals(assembly.Info.Slug, ContractSlug, StringComparison.Ordinal)) {
                        contract.Add(match);
                    } else {
                        elsewhere.Add(match);
                    }
                }
            }
        }

        return contract.Count switch {
            1 => new ReferenceTypeLookup(contract[0], [], null),
            > 1 => new ReferenceTypeLookup(null, contract, null),
            _ => new ReferenceTypeLookup(null, [], elsewhere.FirstOrDefault()),
        };
    }

    /// <summary>Searches the page contract's names and summaries.</summary>
    /// <param name="query">What to look for, case-insensitively.</param>
    /// <param name="limit">The most matches to return.</param>
    public (IReadOnlyList<ReferenceSearchMatch> Matches, bool Truncated) Search(string query, int limit) {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        ReferenceAssembly? contract = Find(ContractSlug);
        if (contract is null) {
            return ([], false);
        }

        List<(int Rank, int Order, ReferenceSearchMatch Match)> found = [];
        int order = 0;

        foreach (XElement type in contract.Document.Descendants("type")) {
            string typeName = (string?)type.Attribute("name") ?? string.Empty;
            string typeDisplay = (string?)type.Attribute("display") ?? typeName;
            string summary = Summary(type);

            int rank = Rank(query, typeName, typeDisplay, summary, member: null);
            if (rank >= 0) {
                found.Add((rank, order++, new ReferenceSearchMatch(typeDisplay, null, "Type", summary)));
            }

            foreach (XElement member in type.Elements("member")) {
                string display = (string?)member.Attribute("display") ?? string.Empty;
                string memberName = (string?)member.Attribute("display") ?? display;
                string memberSummary = Summary(member);
                int memberRank = Rank(query, typeName, display, memberSummary, memberName);
                if (memberRank >= 0) {
                    found.Add((memberRank, order++, new ReferenceSearchMatch(
                        typeDisplay,
                        display,
                        (string?)member.Attribute("kind") ?? "Member",
                        memberSummary)));
                }
            }
        }

        List<ReferenceSearchMatch> ordered = [.. found
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Order)
            .Select(item => item.Match)];

        return ordered.Count <= limit
            ? (ordered, false)
            : ([.. ordered.Take(limit)], true);
    }

    /// <summary>The page contract's document, for the tool, or null when the build carries none.</summary>
    public ReferenceAssembly? Contract => Find(ContractSlug);

    private static int Rank(string query, string typeName, string display, string summary, string? member) {
        if (string.Equals(query, typeName, StringComparison.OrdinalIgnoreCase)) {
            return 0;
        }

        if (typeName.Contains(query, StringComparison.OrdinalIgnoreCase)) {
            return 1;
        }

        if (member is not null && (member.Contains(query, StringComparison.OrdinalIgnoreCase)
            || display.Contains(query, StringComparison.OrdinalIgnoreCase))) {
            return 2;
        }

        return summary.Contains(query, StringComparison.OrdinalIgnoreCase) ? 3 : -1;
    }

    private static string NormalizeTypeQuery(string name) {
        string wanted = name.Trim();
        if (wanted.StartsWith("T:", StringComparison.Ordinal)) {
            wanted = wanted[2..];
        }

        int angle = wanted.IndexOf('<', StringComparison.Ordinal);
        if (angle >= 0) {
            wanted = wanted[..angle];
        }

        int tick = wanted.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? wanted : wanted[..tick];
    }

    private static bool TypeMatches(XElement type, string wanted) {
        string? id = (string?)type.Attribute("id");
        string? name = (string?)type.Attribute("name");
        string? local = (string?)type.Attribute("local");
        string? display = (string?)type.Attribute("display");

        return Matches(id) || Matches(name) || Matches(local) || Matches(display);

        bool Matches(string? value) {
            if (value is null) {
                return false;
            }

            string candidate = value;
            int angle = candidate.IndexOf('<', StringComparison.Ordinal);
            if (angle >= 0) {
                candidate = candidate[..angle];
            }

            int tick = candidate.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0) {
                candidate = candidate[..tick];
            }

            return string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, "T:" + wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "T:" + wanted, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Summary(XElement element) {
        XElement? summary = element.Element("summary");
        return summary is null ? string.Empty : ReferenceText.Flatten(summary).Trim();
    }

    private Corpus Load() {
        List<ReferenceDocument> documents = [];
        foreach (ReferenceAssemblyInfo info in ReferenceAssemblyInfo.All) {
            string path = Path.Combine(_directory, info.AssemblyName + ".xml");
            if (!File.Exists(path)) {
                _logger.LogDebug("This build carries no documentation for {Assembly}", info.AssemblyName);
                continue;
            }

            try {
                using FileStream stream = File.OpenRead(path);
                using XmlReader reader = XmlReader.Create(
                    stream,
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                documents.Add(ReferenceDocument.Build(info, XDocument.Load(reader)));
            } catch (Exception exception) when (exception is IOException or XmlException or InvalidDataException) {
                _logger.LogWarning(
                    exception,
                    "Could not read the documentation for {Assembly}; the reference will leave it out",
                    info.AssemblyName);
            }
        }

        ReferenceInheritance.Resolve(documents, _logger);
        AnnotateCrefs(documents);

        List<ReferenceAssembly> assemblies = [.. documents.Select(document => new ReferenceAssembly(
            document.Info,
            document.TypeCount,
            document.EntryCount,
            document.Content,
            new Lazy<string>(
                () => ReferenceRenderer.Render(document.Content),
                LazyThreadSafetyMode.ExecutionAndPublication)))];

        return new Corpus(assemblies);
    }

    // One pass over every document, after inheritdoc has copied text, so a cref inside copied text
    // is resolved exactly like one the author wrote.
    private static void AnnotateCrefs(IReadOnlyList<ReferenceDocument> documents) {
        Dictionary<string, (string Slug, ReferenceEntry Entry)> targets = [];
        foreach (ReferenceDocument document in documents) {
            foreach ((string id, ReferenceEntry entry) in document.Entries) {
                targets[id] = (document.Info.Slug, entry);
            }
        }

        foreach (ReferenceDocument document in documents) {
            foreach (XElement element in document.Content.Descendants()
                .Where(item => item.Name == "see" || item.Name == "exception")) {
                if ((string?)element.Attribute("cref") is not { Length: > 0 } cref) {
                    continue;
                }

                bool hasContent = element.Nodes().Any();
                if (targets.TryGetValue(cref, out (string Slug, ReferenceEntry Entry) target)) {
                    element.SetAttributeValue(
                        "href",
                        "/admin/reference/" + target.Slug + "#" + target.Entry.Anchor);
                    if (!hasContent) {
                        element.SetAttributeValue("text", target.Entry.Text);
                    }
                } else {
                    string text = ReferenceDisplay.CrefText(cref);
                    if (element.Name == "exception" || !hasContent) {
                        element.SetAttributeValue("text", text);
                    }

                    if (ReferenceDisplay.LearnUrl(cref) is { } learn) {
                        element.SetAttributeValue("href", learn);
                    }
                }
            }
        }
    }

    private sealed record Corpus(IReadOnlyList<ReferenceAssembly> Assemblies);
}
