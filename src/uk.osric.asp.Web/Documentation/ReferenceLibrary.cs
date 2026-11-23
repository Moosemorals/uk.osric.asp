// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Documentation;

using System.Collections.Concurrent;
using System.Text.Json;
using uk.osric.asp.Core.Text;
using uk.osric.asp.Sdk;

/// <summary>One assembly's documentation as the site serves it.</summary>
/// <remarks>A class rather than a record: the rendered fragments are mutable state, not part of the value.</remarks>
internal sealed class ReferenceAssembly {

    private readonly ConcurrentDictionary<string, Lazy<string>> _rendered = new(StringComparer.Ordinal);

    /// <summary>Reads one assembly's corpus.</summary>
    /// <param name="info">The assembly it describes.</param>
    /// <param name="corpus">The corpus the build emitted.</param>
    public ReferenceAssembly(ReferenceAssemblyInfo info, ReferenceCorpus corpus) {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(corpus);

        Info = info;
        Corpus = corpus;
    }

    /// <summary>The assembly it describes.</summary>
    public ReferenceAssemblyInfo Info { get; }

    /// <summary>The corpus the build emitted.</summary>
    public ReferenceCorpus Corpus { get; }

    /// <summary>How many type sections the corpus holds.</summary>
    public int TypeCount => Corpus.TypeCount;

    /// <summary>How many documented entries the source file carried.</summary>
    public int EntryCount => Corpus.EntryCount;

    /// <summary>One namespace's fragment, rendered once and held for the process.</summary>
    /// <param name="space">The namespace to render.</param>
    public string Render(ReferenceNamespace space) {
        ArgumentNullException.ThrowIfNull(space);

        return _rendered.GetOrAdd(
            space.Name,
            _ => new Lazy<string>(
                () => ReferenceHtml.Namespace(space),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
}

/// <summary>A type the tool found, with the document it belongs to.</summary>
/// <param name="Assembly">The assembly.</param>
/// <param name="Type">The type.</param>
internal sealed record ReferenceTypeMatch(ReferenceAssembly Assembly, ReferenceType Type);

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
/// The JSON cannot change while the image runs, so one read is enough. Loading is lazy: a host that
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
    /// <param name="directory">Where the assemblies' JSON files sit.</param>
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

    /// <summary>The corpora this build carries, in reading order, as a page reads them.</summary>
    public IReadOnlyList<ReferenceCorpus> Corpora => [.. Assemblies.Select(assembly => assembly.Corpus)];

    /// <summary>One assembly by its URL segment, or null when this build does not carry it.</summary>
    /// <param name="slug">The segment under <c>/admin/reference</c>.</param>
    public ReferenceAssembly? Find(string slug) =>
        Assemblies.FirstOrDefault(assembly => string.Equals(assembly.Info.Slug, slug, StringComparison.Ordinal));

    /// <summary>One entry's page, when the corpus holds it.</summary>
    /// <param name="slug">The assembly's URL segment.</param>
    /// <param name="anchor">The entry's fragment.</param>
    public bool HasAnchor(string slug, string anchor) =>
        Find(slug) is { } assembly && assembly.Corpus.Namespaces
            .SelectMany(space => space.Types)
            .Any(type => string.Equals(type.Anchor, anchor, StringComparison.Ordinal)
                || type.Members.Any(member => string.Equals(member.Anchor, anchor, StringComparison.Ordinal)));

    /// <summary>A type by local name, relative name, generic form or raw id.</summary>
    /// <param name="name">What the model named.</param>
    public ReferenceTypeLookup FindType(string name) {
        ArgumentNullException.ThrowIfNull(name);

        string wanted = NormalizeTypeQuery(name);
        List<ReferenceTypeMatch> contract = [];
        List<ReferenceTypeMatch> elsewhere = [];

        foreach (ReferenceAssembly assembly in Assemblies) {
            foreach (ReferenceType type in assembly.Corpus.Namespaces.SelectMany(space => space.Types)) {
                if (TypeMatches(type, wanted)) {
                    ReferenceTypeMatch match = new(assembly, type);
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

        foreach (ReferenceType type in contract.Corpus.Namespaces.SelectMany(space => space.Types)) {
            string summary = type.Summary.Text;

            int rank = Rank(query, type.Name, type.Display, summary, member: null);
            if (rank >= 0) {
                found.Add((rank, order++, new ReferenceSearchMatch(type.Display, null, "Type", summary)));
            }

            foreach (ReferenceMember member in type.Members) {
                int memberRank = Rank(query, type.Name, member.Declaration.Text, member.Summary.Text, member.Declaration.Text);
                if (memberRank >= 0) {
                    found.Add((memberRank, order++, new ReferenceSearchMatch(
                        type.Display,
                        member.Declaration.Text,
                        member.Kind,
                        member.Summary.Text)));
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

    /// <summary>The page contract's corpus, for the tool, or null when the build carries none.</summary>
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

    private static bool TypeMatches(ReferenceType type, string wanted) {
        return Matches(type.Id) || Matches(type.Name) || Matches(type.Display);

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

    private Corpus Load() {
        List<ReferenceAssembly> assemblies = [];
        foreach (ReferenceAssemblyInfo info in ReferenceAssemblyInfo.All) {
            string path = Path.Combine(_directory, "reference." + info.Slug + ".json");
            if (!File.Exists(path)) {
                _logger.LogDebug("This build carries no documentation for {Assembly}", info.AssemblyName);
                continue;
            }

            try {
                ReferenceCorpus? corpus = JsonSerializer.Deserialize<ReferenceCorpus>(File.ReadAllText(path), JsonSerialization.Options);
                if (corpus is null) {
                    throw new InvalidDataException($"{Path.GetFileName(path)} holds no corpus.");
                }

                assemblies.Add(new ReferenceAssembly(info, corpus));
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or JsonException or InvalidDataException) {
                _logger.LogWarning(
                    exception,
                    "Could not read the documentation for {Assembly}; the reference will leave it out",
                    info.AssemblyName);
            }
        }

        return new Corpus(assemblies);
    }

    private sealed record Corpus(IReadOnlyList<ReferenceAssembly> Assemblies);
}
