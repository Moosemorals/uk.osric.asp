// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;

// Checks docs/decisions.md as a record rather than as prose, because a naming scheme nobody verifies
// is a naming scheme that drifts. It reads the area list out of the file itself, so the list has one
// home, and reports what a reader would otherwise find by hand: a heading that is not a name in one of
// those areas, a name used twice, an entry with no record of where it is recorded, a retired name taken
// again, and a document citing a decision that is not there. Problems print in the file:line shape a
// problem matcher reads.
if (args.Length == 0) {
    Console.Error.WriteLine("usage: check-decisions <repository-root>");
    return 2;
}

string root = Path.GetFullPath(args[0]);
string record = Path.Combine(root, "docs", "decisions.md");
if (!File.Exists(record)) {
    Console.Error.WriteLine($"check-decisions: {record} is missing");
    return 2;
}

string report = Path.GetRelativePath(root, record);
List<Problem> problems = [];

// Tokens that are shaped like a name and are not one: a make target, a DOM id, the policy's own
// counter-examples. An entry nothing cites any more is a problem, so the list cannot rot.
string ignorePath = Path.Combine(root, "tools", "check-decisions", "ignore.txt");
Dictionary<string, int> ignore = [];
HashSet<string> ignored = [];
if (File.Exists(ignorePath)) {
    string[] ignoreLines = File.ReadAllLines(ignorePath);
    for (int i = 0; i < ignoreLines.Length; i++) {
        int hash = ignoreLines[i].IndexOf('#', StringComparison.Ordinal);
        string token = (hash >= 0 ? ignoreLines[i][..hash] : ignoreLines[i]).Trim();
        if (!string.IsNullOrEmpty(token)) {
            ignore[token] = i + 1;
        }
    }
}

// The sections of the record that are policy rather than decisions.
// A set of strings with no comparer is ordinal already, which is what an identifier wants.
HashSet<string> prose = [
    "what a name is",
    "taking a name",
    "keeping it short",
    "retired",
    "open questions",
];

string[] lines = File.ReadAllLines(record);
HashSet<string> areas = Areas(lines, problems);
Sections(lines, report, prose, areas, problems);
List<Entry> entries = Entries(lines, report, prose, areas, problems);

Dictionary<string, Entry> byName = [];
foreach (Entry entry in entries) {
    if (!byName.TryAdd(entry.Name, entry)) {
        problems.Add(new Problem(report, entry.Line, $"`{entry.Name}` is used twice; the first is at line {byName[entry.Name].Line}"));
    }
}

Retired(lines, report, entries, problems);

int citations = 0;
foreach (string file in Sources(root)) {
    citations += Cite(file, root, areas, ignore, ignored, byName, problems);
}

foreach ((string token, int at) in ignore.Where(t => !ignored.Contains(t.Key)).OrderBy(t => t.Value)) {
    problems.Add(new Problem("tools/check-decisions/ignore.txt", at, $"`{token}` is ignored but nothing cites it"));
}

foreach (Problem problem in problems.OrderBy(p => p.File, StringComparer.Ordinal).ThenBy(p => p.Line)) {
    Console.Error.WriteLine($"{problem.File}:{problem.Line}: {problem.Message}");
}

if (problems.Count > 0) {
    Console.Error.WriteLine($"check-decisions: {problems.Count} problem(s)");
    return 1;
}

Console.WriteLine($"decisions: {entries.Count} entries in {areas.Count} areas, {citations} citations resolved");
return 0;

// The area list is read from the bullets under "What a name is", so adding an area is one edit here
// and the check follows it without being told.
static HashSet<string> Areas(string[] lines, List<Problem> problems) {
    HashSet<string> areas = [];
    bool inList = false;
    foreach (string line in lines) {
        if (line.StartsWith("## ", StringComparison.Ordinal)) {
            inList = line[3..].Trim().Equals("What a name is", StringComparison.OrdinalIgnoreCase);
            continue;
        }

        if (!inList || !line.StartsWith("- `", StringComparison.Ordinal)) {
            continue;
        }

        int close = line.IndexOf('`', 3);
        if (close > 3) {
            areas.Add(line[3..close]);
        }
    }

    if (areas.Count == 0) {
        problems.Add(new Problem("docs/decisions.md", 1, "no area list under \"What a name is\""));
    }

    return areas;
}

// Every second-level heading is an area or one of the policy sections; a fourth area cannot arrive
// by typo.
static void Sections(string[] lines, string report, HashSet<string> prose, HashSet<string> areas, List<Problem> problems) {
    HashSet<string> seen = [];
    for (int i = 0; i < lines.Length; i++) {
        if (!lines[i].StartsWith("## ", StringComparison.Ordinal)) {
            continue;
        }

        string section = lines[i][3..].Trim().ToLowerInvariant();
        if (prose.Contains(section)) {
            continue;
        }

        if (!areas.Contains(section)) {
            problems.Add(new Problem(report, i + 1, $"section \"{section}\" is not one of the areas"));
        } else if (!seen.Add(section)) {
            problems.Add(new Problem(report, i + 1, $"area \"{section}\" has two sections"));
        }
    }
}

static List<Entry> Entries(string[] lines, string report, HashSet<string> prose, HashSet<string> areas, List<Problem> problems) {
    List<Entry> entries = [];
    string section = "";
    int start = -1;
    string name = "";
    int line = 0;
    for (int i = 0; i <= lines.Length; i++) {
        if (i == lines.Length || lines[i].StartsWith('#')) {
            if (start >= 0) {
                bool recorded = false;
                for (int j = start; j < i; j++) {
                    recorded |= lines[j].StartsWith("*Recorded in ", StringComparison.Ordinal);
                }

                if (!recorded) {
                    problems.Add(new Problem(report, line, $"`{name}` has no \"*Recorded in …*\" line"));
                }

                start = -1;
            }

            if (i == lines.Length) {
                break;
            }
        }

        string text = lines[i];
        if (text.StartsWith("## ", StringComparison.Ordinal)) {
            section = text[3..].Trim().ToLowerInvariant();
            continue;
        }

        if (!text.StartsWith("### ", StringComparison.Ordinal)) {
            continue;
        }

        name = text[4..].Trim();
        line = i + 1;
        start = line;
        if (prose.Contains(section) || !areas.Contains(section)) {
            problems.Add(new Problem(report, line, $"`{name}` is not under an area"));
        }

        if (!Program.Name().IsMatch(name)) {
            problems.Add(new Problem(report, line, $"`{name}` is not a lowercase hyphenated name"));
        } else if (!areas.Contains(name[..name.IndexOf('-', StringComparison.Ordinal)])) {
            problems.Add(new Problem(report, line, $"`{name}` does not start with one of the areas"));
        }

        entries.Add(new Entry(line, name));
    }

    return entries;
}

// A retired name is kept so it is not taken again, which is worth checking: the one thing a name must
// never do is mean two things.
static void Retired(string[] lines, string report, List<Entry> entries, List<Problem> problems) {
    HashSet<string> live = [.. entries.Select(e => e.Name)];
    string section = "";
    for (int i = 0; i < lines.Length; i++) {
        string line = lines[i];
        if (line.StartsWith("## ", StringComparison.Ordinal)) {
            section = line[3..].Trim();
            continue;
        }

        if (!section.Equals("Retired", StringComparison.OrdinalIgnoreCase)) {
            continue;
        }

        foreach (Match match in Program.Tokens().Matches(line)) {
            string name = match.Groups["name"].Value;
            if (live.Contains(name)) {
                problems.Add(new Problem(report, i + 1, $"retired name `{name}` is also an entry"));
            }
        }
    }
}

// A citation is a backticked token shaped like a name whose first hyphen begins an area: those have to
// be in the record, or the reader is sent to a heading that is not there.
static int Cite(string file, string root, HashSet<string> areas, Dictionary<string, int> ignore, HashSet<string> ignored, Dictionary<string, Entry> byName, List<Problem> problems) {
    int citations = 0;
    string shown = Path.GetRelativePath(root, file);
    string section = "";
    string[] lines = File.ReadAllLines(file);
    for (int i = 0; i < lines.Length; i++) {
        string line = lines[i];
        if (shown.EndsWith(".md", StringComparison.Ordinal) && line.StartsWith("## ", StringComparison.Ordinal)) {
            section = line[3..].Trim();
        }

        // The retired list names decisions on purpose, and by definition they are not entries.
        if (section.Equals("Retired", StringComparison.OrdinalIgnoreCase)) {
            continue;
        }

        foreach (Match match in Program.Tokens().Matches(line)) {
            string name = match.Groups["name"].Value;
            if (!areas.Contains(name[..name.IndexOf('-', StringComparison.Ordinal)])) {
                continue;
            }

            if (ignore.ContainsKey(name)) {
                ignored.Add(name);
                continue;
            }

            citations++;
            if (!byName.ContainsKey(name)) {
                problems.Add(new Problem(shown, i + 1, $"`{name}` is cited but is not in the record"));
            }
        }
    }

    return citations;
}

static IEnumerable<string> Sources(string root) {
    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
        string[] parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
        if (parts.Any(part => part is "artifacts" or ".git" or "bin" or "obj" or "node_modules")) {
            continue;
        }

        if (file.EndsWith(".md", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal)) {
            yield return file;
        }
    }
}

internal sealed record Entry(int Line, string Name);

internal sealed record Problem(string File, int Line, string Message);

internal static partial class Program {

    [GeneratedRegex(@"^[a-z][a-z0-9]*(-[a-z0-9]+)+$")]
    internal static partial Regex Name();

    [GeneratedRegex("`(?<name>[a-z][a-z0-9]*(?:-[a-z0-9]+)+)`")]
    internal static partial Regex Tokens();
}
