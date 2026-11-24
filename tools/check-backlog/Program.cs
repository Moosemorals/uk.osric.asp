// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;

// Checks docs/backlog as a record rather than as prose, because a scheme nobody verifies is a scheme
// that drifts. It reads the difficulty scale out of the backlog index, the order out of docs/README.md
// and the decision areas out of the record, so each list has one home, and reports what a reader would
// otherwise find by hand: a slug outside the shape, a slug used twice, a missing field, an item missing
// from the order or one that has no shape being in it, a row that disagrees with its item, an order
// that puts an item above its blocker, a cycle, a plan that exists twice or does not link back, a
// withdrawn slug still cited, and a citation of an item that is not in the record. Problems print in
// the file:line shape a problem matcher reads.
if (args.Length == 0) {
    Console.Error.WriteLine("usage: check-backlog <repository-root>");
    return 2;
}

string root = Path.GetFullPath(args[0]);
string directory = Path.Combine(root, "docs", "backlog");
string index = Path.Combine(directory, "README.md");
if (!File.Exists(index)) {
    Console.Error.WriteLine($"check-backlog: {index} is missing");
    return 2;
}

// The order is the site's documentation index, so the table has one home whether it is read by a
// person or by this check.
string orderIndex = Path.Combine(root, "docs", "README.md");
if (!File.Exists(orderIndex)) {
    Console.Error.WriteLine($"check-backlog: {orderIndex} is missing");
    return 2;
}

const string NotSet = "—";
string shownIndex = Path.GetRelativePath(root, index);
string shownOrder = Path.GetRelativePath(root, orderIndex);
List<Problem> problems = [];

HashSet<string> areas = DecisionAreas(root);
HashSet<string> utilities = ["low", "medium", "high"];
HashSet<string> statuses = ["unshaped", "open", "planned", "blocked", "parked"];

string[] indexLines = File.ReadAllLines(index);
List<string> difficulties = DifficultyScale(indexLines);
if (difficulties.Count == 0) {
    problems.Add(new(shownIndex, 1, "no difficulty scale under \"Difficulty is a level, not a duration\""));
}

List<Item> items = Items(root, directory);
Dictionary<string, Item> bySlug = [];
foreach (Item item in items) {
    if (!bySlug.TryAdd(item.Slug, item)) {
        problems.Add(new(item.File, item.Line, $"`{item.Slug}` is used twice; the first is at line {bySlug[item.Slug].Line}"));
    }
}

(HashSet<string> closedSlugs, HashSet<string> closedNumbers) = ClosedItems(Path.Combine(directory, "closed.md"));

foreach (Item item in items) {
    if (!Program.Slug().IsMatch(item.Slug)) {
        problems.Add(new(item.File, item.Line, $"`{item.Slug}` is not a lowercase hyphenated slug"));
    } else {
        // A defect's slug opens with its kind and an idea's opens with anything but one, so the only
        // word that means something is the one no item may use: an area would read as a decision.
        string first = item.Slug[..item.Slug.IndexOf('-', StringComparison.Ordinal)];
        if (areas.Contains(first)) {
            problems.Add(new(item.File, item.Line, $"`{item.Slug}` begins with a decision area"));
        }
    }

    if (item.Title.Length == 0) {
        problems.Add(new(item.File, item.Line, "no H1 title"));
    } else if (Program.LegacyTitle().IsMatch(item.Title)) {
        problems.Add(new(item.File, item.Line, "the H1 still carries an old number; the H1 is the title"));
    }

    Present(item, "Difficulty", difficulties, problems, mayBeUnset: true);
    Present(item, "Utility", utilities, problems, mayBeUnset: true);
    Present(item, "Status", statuses, problems);
    Present(item, "Blocked by", null, problems, mayBeUnset: true);

    bool unshaped = Unshaped(item);
    if ((item.Header.GetValueOrDefault("Difficulty") == NotSet) != (item.Header.GetValueOrDefault("Utility") == NotSet)) {
        problems.Add(new(item.File, item.HeaderLine("Difficulty"), "`Difficulty` and `Utility` answer together: both a value or both `—`"));
    }

    Present(item, "Area", null, problems, mayBeUnset: unshaped);
    Present(item, "Location", null, problems, mayBeUnset: unshaped);

    if (unshaped) {
        if (item.Header.GetValueOrDefault("Blocked by") is { } cell && cell != NotSet) {
            problems.Add(new(item.File, item.HeaderLine("Blocked by"), "an unshaped item cannot be blocked; shape it first"));
        }

        if (item.Plan) {
            problems.Add(new(item.File, item.Line, "an unshaped item has a `## Plan` section; shape it first"));
        }

        if (item.Header.ContainsKey("Plan")) {
            problems.Add(new(item.File, item.HeaderLine("Plan"), "an unshaped item has a `Plan` row; shape it first"));
        }
    }

    PlanRow(item, problems);
}

Dictionary<string, List<string>> blocks = [];
foreach (Item item in items) {
    List<string> targets = [];
    if (item.Header.TryGetValue("Blocked by", out string? cell) && cell != NotSet) {
        foreach (Match match in Program.Token().Matches(cell)) {
            string token = match.Groups["slug"].Value;
            targets.Add(token);
            if (bySlug.ContainsKey(token) || closedSlugs.Contains(token)) {
                continue;
            }

            if (closedNumbers.Contains(token)) {
                continue;
            }

            problems.Add(new(item.File, item.HeaderLine("Blocked by"), $"`{token}` is not an item and is not in closed.md"));
        }
    }

    blocks[item.Slug] = targets;
}

List<Row> rows = Order(File.ReadAllLines(orderIndex));
Dictionary<string, Row> rowOf = [];
foreach (Row row in rows) {
    foreach (string slug in row.Slugs) {
        if (!rowOf.TryAdd(slug, row)) {
            problems.Add(new(shownOrder, row.Line, $"`{slug}` is listed twice; the first is row {rowOf[slug].Number}"));
        }
    }
}

foreach (Item item in items) {
    if (!rowOf.TryGetValue(item.Slug, out Row? row)) {
        if (!Unshaped(item)) {
            problems.Add(new(item.File, item.Line, $"`{item.Slug}` is not in the order"));
        }

        continue;
    }

    if (Unshaped(item)) {
        problems.Add(new(shownOrder, row.Line, $"`{item.Slug}` is unshaped and is in the order; shape it first"));
        continue;
    }

    if (row.Slugs.Count > 1) {
        if (Rank(row.Difficulty) < Rank(item.Header.GetValueOrDefault("Difficulty"))) {
            problems.Add(new(shownOrder, row.Line, $"the step is {row.Difficulty} and `{item.Slug}` is harder"));
        }

        if (!string.Equals(row.Utility, item.Header.GetValueOrDefault("Utility"), StringComparison.Ordinal)) {
            problems.Add(new(shownOrder, row.Line, $"the step is {row.Utility} and `{item.Slug}` is {item.Header.GetValueOrDefault("Utility")}"));
        }

        continue;
    }

    if (!string.Equals(row.Title, item.Title, StringComparison.Ordinal)) {
        problems.Add(new(shownOrder, row.Line, $"the row is titled \"{row.Title}\" and `{item.Slug}` is \"{item.Title}\""));
    }

    if (!string.Equals(row.Difficulty, item.Header.GetValueOrDefault("Difficulty"), StringComparison.Ordinal)) {
        problems.Add(new(shownOrder, row.Line, $"the row is {row.Difficulty} and `{item.Slug}` is {item.Header.GetValueOrDefault("Difficulty")}"));
    }

    if (!string.Equals(row.Utility, item.Header.GetValueOrDefault("Utility"), StringComparison.Ordinal)) {
        problems.Add(new(shownOrder, row.Line, $"the row is {row.Utility} and `{item.Slug}` is {item.Header.GetValueOrDefault("Utility")}"));
    }

    if (!string.Equals(row.Status, item.Header.GetValueOrDefault("Status"), StringComparison.Ordinal)) {
        problems.Add(new(shownOrder, row.Line, $"the row is {row.Status} and `{item.Slug}` is {item.Header.GetValueOrDefault("Status")}"));
    }

    if (row.Status == "planned") {
        // The row links what makes the item planned: its own section, or the plan file it still names.
        bool links = item.Plan
            ? row.Target.EndsWith($"{item.Slug}.md#plan", StringComparison.Ordinal)
            : string.Equals(row.Plan, PlanName(item.Header.GetValueOrDefault("Plan") ?? "—"), StringComparison.Ordinal);
        if (!links) {
            problems.Add(new(shownOrder, row.Line, item.Plan
                ? $"the row is planned and does not link `{item.Slug}.md#plan`"
                : $"the row is planned for {row.Plan} and `{item.Slug}` names {PlanName(item.Header.GetValueOrDefault("Plan") ?? "—")}"));
        }
    } else if (row.Target != "—") {
        problems.Add(new(shownOrder, row.Line, $"the row is {row.Status} and links a plan; only planned needs one"));
    }
}

foreach (Row row in rows) {
    if (!row.Reason) {
        problems.Add(new(shownOrder, row.Line, "the row has no reason line under it"));
    }

    foreach (string slug in row.Slugs) {
        if (!bySlug.ContainsKey(slug)) {
            problems.Add(new(shownOrder, row.Line, $"`{slug}` is in the order and is not an item"));
        }
    }
}

// Status is derived from the graph and the plan and then checked, so the field cannot become a
// second, staler opinion; the order is then checked against the same graph.
foreach (Item item in items) {
    string expected = Unshaped(item) ? "unshaped"
        : blocks[item.Slug].Any(bySlug.ContainsKey) ? "blocked"
        : Planned(item) ? "planned"
        : "open";
    string? stored = item.Header.GetValueOrDefault("Status");
    if (stored != "parked" && !string.Equals(stored, expected, StringComparison.Ordinal)) {
        problems.Add(new(item.File, item.HeaderLine("Status"), $"says {stored} and the record says {expected}"));
    }

    if (!rowOf.TryGetValue(item.Slug, out Row? row)) {
        continue;
    }

    foreach (string blocker in blocks[item.Slug].Where(bySlug.ContainsKey)) {
        if (rowOf.TryGetValue(blocker, out Row? other) && other.Number >= row.Number) {
            problems.Add(new(shownOrder, row.Line, $"`{item.Slug}` is blocked by `{blocker}`, which is at row {other.Number}"));
        }
    }
}

Cycles(blocks, bySlug, problems);
Plans(directory, items, problems);
int citations = Citations(root, bySlug, (closedSlugs, closedNumbers), problems);

foreach (Problem problem in problems.OrderBy(p => p.File, StringComparer.Ordinal).ThenBy(p => p.Line)) {
    Console.Error.WriteLine($"{problem.File}:{problem.Line}: {problem.Message}");
}

if (problems.Count > 0) {
    Console.Error.WriteLine($"check-backlog: {problems.Count} problem(s)");
    return 1;
}

Console.WriteLine($"backlog: {items.Count} items ({items.Count(Unshaped)} unshaped) in {rows.Count} rows, {citations} citations resolved");
return 0;

int Rank(string? level) {
    int index = level is null ? -1 : difficulties.IndexOf(level);
    return index < 0 ? 0 : index + 1;
}

// The area list is read out of the decision record, so the two namespaces cannot overlap for long.
static HashSet<string> DecisionAreas(string root) {
    HashSet<string> found = [];
    string record = Path.Combine(root, "docs", "decisions.md");
    if (!File.Exists(record)) {
        return found;
    }

    bool inList = false;
    foreach (string line in File.ReadAllLines(record)) {
        if (line.StartsWith("## ", StringComparison.Ordinal)) {
            inList = line[3..].Trim().Equals("What a name is", StringComparison.OrdinalIgnoreCase);
            continue;
        }

        if (inList && line.StartsWith("- `", StringComparison.Ordinal)) {
            int close = line.IndexOf('`', 3);
            if (close > 3) {
                found.Add(line[3..close]);
            }
        }
    }

    return found;
}

// The scale is the numbered table under its heading, so a level is added in one place and the check
// follows it without being told.
static List<string> DifficultyScale(string[] lines) {
    List<string> scale = [];
    bool inScale = false;
    foreach (string line in lines) {
        if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal)) {
            inScale = line.Contains("Difficulty is a level", StringComparison.Ordinal);
            continue;
        }

        Match match = Program.ScaleRow().Match(line);
        if (inScale && match.Success) {
            scale.Add(match.Groups["level"].Value);
        }
    }

    return scale;
}

static List<Item> Items(string root, string directory) {
    List<Item> found = [];
    foreach (string file in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)) {
        string slug = Path.GetFileNameWithoutExtension(file);
        if (slug is "README" or "closed") {
            continue;
        }

        found.Add(Item(root, file, slug));
    }

    return found;
}

static Item Item(string root, string file, string slug) {
    string[] lines = File.ReadAllLines(file);
    string title = "";
    int line = 1;
    for (int i = 0; i < lines.Length; i++) {
        if (lines[i].StartsWith("# ", StringComparison.Ordinal)) {
            title = lines[i][2..].Trim();
            line = i + 1;
            break;
        }
    }

    Dictionary<string, string> header = [];
    Dictionary<string, int> at = [];
    for (int i = 0; i + 1 < lines.Length; i++) {
        if (lines[i] != "| | |" || lines[i + 1] != "| --- | --- |") {
            continue;
        }

        for (int j = i + 2; j < lines.Length && lines[j].StartsWith('|'); j++) {
            Match match = Program.HeaderRow().Match(lines[j]);
            if (match.Success) {
                string key = match.Groups["key"].Value;
                header[key] = match.Groups["value"].Value.Trim();
                at.TryAdd(key, j + 1);
            }
        }

        break;
    }

    return new(Path.GetRelativePath(root, file), slug, line, title, header, at,
        lines.Any(text => string.Equals(text.Trim(), "## Plan", StringComparison.Ordinal)));
}

// A field is present and, where it has a closed set, one of the values in it. A field the shape
// supplies may be an em dash instead, which is what an item with no answers yet leaves.
static void Present(Item item, string key, IReadOnlyCollection<string>? allowed, List<Problem> problems, bool mayBeUnset = false) {
    if (!item.Header.TryGetValue(key, out string? value) || value.Length == 0) {
        problems.Add(new(item.File, item.Line, $"no `{key}` row in the header"));
        return;
    }

    if (value == NotSet) {
        if (!mayBeUnset) {
            problems.Add(new(item.File, item.HeaderLine(key), $"`{key}` is `—` on a shaped item"));
        }

        return;
    }

    if (allowed is { Count: > 0 } && !allowed.Contains(value)) {
        problems.Add(new(item.File, item.HeaderLine(key), $"`{value}` is not one of {string.Join(", ", allowed)}"));
    }
}

// A shape is four answers or none. An item with neither is a whim: it has no difficulty to rank and no
// utility to sort by, so it is not work yet and nothing may depend on it.
static bool Unshaped(Item item) =>
    item.Header.GetValueOrDefault("Difficulty") == NotSet && item.Header.GetValueOrDefault("Utility") == NotSet;

// A plan is the item's own section, or a file in docs/plans written before the file became one.
static bool Planned(Item item) => item.Plan || Live(item);

// A `Plan` row survives only for those older plans, so it is a link and nothing else; an item's own
// plan is a section and needs no row. When the last of the seven lands, the row goes with it.
static void PlanRow(Item item, List<Problem> problems) {
    if (!item.Header.TryGetValue("Plan", out string? cell)) {
        return;
    }

    if (cell == NotSet) {
        problems.Add(new(item.File, item.HeaderLine("Plan"), "the `Plan` row says `—`; drop the row or link the plan it names"));
        return;
    }

    if (item.Plan) {
        problems.Add(new(item.File, item.HeaderLine("Plan"), "the item has a `## Plan` section and a `Plan` row; one or the other"));
    }
}

static List<Row> Order(string[] lines) {
    List<Row> found = [];
    bool inOrder = false;
    for (int i = 0; i < lines.Length; i++) {
        if (lines[i].StartsWith("## ", StringComparison.Ordinal)) {
            inOrder = lines[i][3..].Trim().Equals("The order", StringComparison.OrdinalIgnoreCase);
            continue;
        }

        Match match = Program.OrderRow().Match(lines[i]);
        if (!inOrder || !match.Success) {
            continue;
        }

        List<string> slugs = [];
        foreach (Match token in Program.Token().Matches(match.Groups["slugs"].Value)) {
            slugs.Add(token.Groups["slug"].Value);
        }

        // The header and the separator are shaped like a row now that a number no longer opens one,
        // and a row that names no item is the table's furniture rather than an entry.
        if (slugs.Count == 0) {
            continue;
        }

        string status = Status(match.Groups["status"].Value);
        found.Add(new(i + 1, found.Count + 1, match.Groups["title"].Value.Trim(), Level(match.Groups["difficulty"].Value),
            match.Groups["utility"].Value.Trim(), status, PlanName(match.Groups["status"].Value),
            Target(match.Groups["status"].Value), slugs, Reason(lines, i)));
    }

    return found;
}

// The line under a row is the reason it sits there. Markdown has no merged cells, so the reason is a
// row of its own with the title column carrying the text; a row without one is a number with no
// argument behind it.
static bool Reason(string[] lines, int row) =>
    row + 1 < lines.Length
    && lines[row + 1].StartsWith("| |", StringComparison.Ordinal)
    && lines[row + 1].Split('|').Any(cell => cell.Trim().Length > 2);

// A plan is live while it is not in the archive, which is how an item that still needs work after
// its plan landed says so.
static bool Live(Item item) {
    if (!item.Header.TryGetValue("Plan", out string? cell) || cell == "—") {
        return false;
    }

    Match match = Program.Link().Match(cell);
    return match.Success && !match.Groups["target"].Value.Contains("plans/archive/", StringComparison.Ordinal);
}

// A planned row carries its plan as the status, so the state and the thing that decides it are one
// cell; every other state is plain text.
static string Status(string cell) {
    Match match = Program.Link().Match(cell);
    return match.Success ? match.Groups["text"].Value.Trim() : cell.Trim();
}

// A plan is compared by its file, because the item and the row name it from different directories.
static string PlanName(string cell) {
    Match match = Program.Link().Match(cell);
    return match.Success ? Path.GetFileName(match.Groups["target"].Value) : "—";
}

// A planned row links its own item's section, so the whole target is what is compared, fragment and all.
static string Target(string cell) {
    Match match = Program.Link().Match(cell);
    return match.Success ? match.Groups["target"].Value : "—";
}

// The cell is the level's slug and then its emoji, so the level is the first word.
static string Level(string cell) {
    int space = cell.IndexOf(' ', StringComparison.Ordinal);
    return space < 0 ? cell.Trim() : cell[..space].Trim();
}

static void Cycles(Dictionary<string, List<string>> blocks, Dictionary<string, Item> bySlug, List<Problem> problems) {
    Dictionary<string, bool> done = [];
    Dictionary<string, bool> walking = [];
    foreach (string slug in bySlug.Keys) {
        Walk(slug);
    }

    void Walk(string slug) {
        if (walking.GetValueOrDefault(slug)) {
            problems.Add(new(bySlug[slug].File, bySlug[slug].Line, $"`{slug}` is in a cycle of blockers"));
            return;
        }

        if (done.GetValueOrDefault(slug)) {
            return;
        }

        walking[slug] = true;
        foreach (string next in blocks.GetValueOrDefault(slug, []).Where(bySlug.ContainsKey)) {
            Walk(next);
        }

        walking[slug] = false;
        done[slug] = true;
    }
}

// A plan and its items point at each other, so neither can move without the other. An archived plan
// is history and is not asked to link back.
static void Plans(string directory, List<Item> items, List<Problem> problems) {
    foreach (Item item in items) {
        if (!item.Header.TryGetValue("Plan", out string? cell) || cell == "—") {
            continue;
        }

        Match match = Program.Link().Match(cell);
        if (!match.Success) {
            problems.Add(new(item.File, item.HeaderLine("Plan"), $"`{cell}` is not a link"));
            continue;
        }

        string target = match.Groups["target"].Value;
        if (target.Contains("plans/archive/", StringComparison.Ordinal)) {
            continue;
        }

        string path = Path.GetFullPath(Path.Combine(directory, target));
        if (!File.Exists(path)) {
            problems.Add(new(item.File, item.HeaderLine("Plan"), $"`{target}` does not exist"));
            continue;
        }

        bool back = File.ReadAllLines(path)
            .Where(line => line.StartsWith("| Backlog |", StringComparison.Ordinal))
            .Any(line => line.Contains($"../backlog/{item.Slug}.md", StringComparison.Ordinal));
        if (!back) {
            problems.Add(new(item.File, item.HeaderLine("Plan"), $"`{Path.GetFileName(target)}` does not link back to `{item.Slug}`"));
        }
    }
}

// A citation is a backticked token that names an item, and an item it does not find is a reader sent
// to a file that is not there. A backticked old number is allowed only where closed.md kept it. An
// idea's slug carries no kind for a token to be recognised by, so a link into the record is what
// proves those: its target has to be a file beside the index.
static int Citations(string root, Dictionary<string, Item> bySlug, (HashSet<string> Slugs, HashSet<string> Numbers) closed, List<Problem> problems) {
    int count = 0;
    string record = Path.Combine(root, "docs", "backlog") + Path.DirectorySeparatorChar;
    foreach (string file in Sources(root)) {
        string shown = Path.GetRelativePath(root, file);
        if (Frozen(shown)) {
            continue;
        }

        string[] lines = File.ReadAllLines(file);
        for (int i = 0; i < lines.Length; i++) {
            foreach (Match match in Program.Citation().Matches(lines[i])) {
                count++;
                string slug = match.Groups["slug"].Value;
                if (!bySlug.ContainsKey(slug) && !closed.Slugs.Contains(slug)) {
                    problems.Add(new(shown, i + 1, $"`{slug}` is cited but is not in the record"));
                }
            }

            foreach (Match match in Program.Withdrawn().Matches(lines[i])) {
                count++;
                problems.Add(new(shown, i + 1, $"`{match.Groups["slug"].Value}` uses a `whim-` prefix the record no longer has; cite the item it became"));
            }

            foreach (Match match in Program.Legacy().Matches(lines[i])) {
                string number = match.Groups["id"].Value;
                if (!closed.Numbers.Contains(number)) {
                    problems.Add(new(shown, i + 1, $"`{number}` is cited by its old number and closed.md has no such row"));
                }
            }

            foreach (Match match in Program.Link().Matches(lines[i])) {
                string target = match.Groups["target"].Value.Split('#')[0];
                if (target.Length == 0 || target.Contains("://", StringComparison.Ordinal)) {
                    continue;
                }

                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, target));
                if (!resolved.StartsWith(record, StringComparison.Ordinal)) {
                    continue;
                }

                count++;
                if (!File.Exists(resolved)) {
                    problems.Add(new(shown, i + 1, $"[{match.Groups["text"].Value}]({match.Groups["target"].Value}) links to a file the record does not have"));
                }
            }
        }
    }

    return count;
}

// The frozen files keep the numbers and the prose a reader was sent to when they were written.
static bool Frozen(string shown) =>
    shown == Path.Combine("docs", "backlog", "closed.md")
    || shown.StartsWith(Path.Combine("docs", "backlog", "archive") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
    || shown.StartsWith(Path.Combine("docs", "archive") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
    || shown.StartsWith(Path.Combine("docs", "plans", "archive") + Path.DirectorySeparatorChar, StringComparison.Ordinal);

static (HashSet<string> Slugs, HashSet<string> Numbers) ClosedItems(string file) {
    HashSet<string> slugs = [];
    HashSet<string> numbers = [];
    if (!File.Exists(file)) {
        return (slugs, numbers);
    }

    foreach (string line in File.ReadAllLines(file)) {
        if (!line.StartsWith("| ", StringComparison.Ordinal)) {
            continue;
        }

        int close = line.IndexOf('|', 2);
        string first = close < 0 ? line[2..] : line[2..close];
        foreach (Match match in Program.Legacy().Matches("`" + first.Trim() + "`")) {
            numbers.Add(match.Groups["id"].Value);
        }

        foreach (Match match in Program.Token().Matches(first)) {
            slugs.Add(match.Groups["slug"].Value);
        }
    }

    return (slugs, numbers);
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

internal sealed record Item(string File, string Slug, int Line, string Title, Dictionary<string, string> Header, Dictionary<string, int> Rows, bool Plan) {
    internal int HeaderLine(string key) => Rows.GetValueOrDefault(key, Line);
}

// Number is the row's position in the table, so the messages can name one without the table carrying a
// column that has to be renumbered by hand.
internal sealed record Row(int Line, int Number, string Title, string Difficulty, string Utility, string Status, string Plan, string Target, List<string> Slugs, bool Reason);

internal sealed record Problem(string File, int Line, string Message);

internal static partial class Program {

    [GeneratedRegex(@"^[a-z][a-z0-9]*(-[a-z0-9]+)+$")]
    internal static partial Regex Slug();

    [GeneratedRegex(@"^(?:IDEA|SEC|OPS|ARCH|BUG)-\d+")]
    internal static partial Regex LegacyTitle();

    [GeneratedRegex(@"^\| (?<key>[^|]+?) \| (?<value>.*) \|$")]
    internal static partial Regex HeaderRow();

    [GeneratedRegex(@"^\| (?<slugs>[^|]+) \| (?<title>[^|]+) \| (?<difficulty>[^|]+) \| (?<utility>[^|]+) \| (?<status>[^|]+) \| (?<blocked>[^|]+) \|")]
    internal static partial Regex OrderRow();

    [GeneratedRegex(@"^\| \d+ \| `(?<level>[a-z-]+)` \|")]
    internal static partial Regex ScaleRow();

    [GeneratedRegex(@"\[(?<text>[^\]]*)\]\((?<target>[^)\s]+)\)")]
    internal static partial Regex Link();

    [GeneratedRegex(@"`(?<slug>[a-z][a-z0-9]*(?:-[a-z0-9]+)+)`")]
    internal static partial Regex Token();

    [GeneratedRegex(@"`(?<slug>(?:bug|sec|ops|arch)-(?=[a-z0-9-]*[a-z])[a-z0-9]+(?:-[a-z0-9]+)*)`")]
    internal static partial Regex Citation();

    [GeneratedRegex(@"`(?<id>(?:IDEA|SEC|OPS|ARCH|BUG)-\d+)`")]
    internal static partial Regex Legacy();

    [GeneratedRegex(@"`(?<slug>whim-[a-z0-9]+(?:-[a-z0-9]+)*)`")]
    internal static partial Regex Withdrawn();
}
