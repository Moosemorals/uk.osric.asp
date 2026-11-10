<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# A kernel for the virtual shell

The plan for the shell the shell became: commands as applets, one kernel that lexes, parses and
expands, and the POSIX rule DEC-224 set. Written before the work and kept afterwards, as DEC-09 asks;
the decisions it landed are DEC-226 and DEC-227.

## Why

The shell grew one command at a time and it shows. Measured today:

| File | Lines | What it is |
| --- | --- | --- |
| `src/uk.osric.asp.Core/Assistant/VirtualShellParser.cs` | 314 | lexer, option table, arity rules and POSIX-by-command quirks in one static class |
| `src/uk.osric.asp.Web/Assistant/ShellHelp.cs` | 96 | the help text, a second copy of the grammar, kept in step with the parser by two tests |
| `src/uk.osric.asp.Web/Assistant/PageToolExecutor.cs` | 1416 | the page tools **and** ten shell commands in one class: `DiffAsync`, `ListAsync`, `GrepAsync`, `ReadAsync`, plus about 30 private members that belong to the shell (`ResolveAsync`, `LineCount`, `Stat`, `NodesAsync`, `ExpandAsync`, `IsInScope`, `IsBelow`, `Mode` ×2, …) |

Four things are wrong with that, and each one is a bug waiting:

1. **A command is spread over three files.** `head`'s options are in `VirtualShellParser.TakeOption`, its arity is in `ValidArguments`, its help is in `ShellHelp`, and its behaviour is `LineCount` + `ReadAsync`. Adding `diff -U n` this week meant editing all four, and the help entry is kept honest only by a test that pins two lists together.
2. **The grammar is parsed four times** — lexing, option/arity, per-command semantics (`grep`'s query, `head`'s count, `diff -U`), and again in help. Later commands (`find`) will want their own mini-language, and there is nowhere to put it.
3. **Two conventions are hacked into the words.** Quoting is discarded by the lexer, so a quoted `*` (added today) is re-encoded as a `\*` inside the word; option *values* (`head -n 4`, `diff -U 2`) are left in the operand list and re-parsed by each command.
4. **Globbing is plumbing, not a stage.** An in-progress change (uncommitted) calls `ExpandAsync` from four places and each command has to remember that a pattern may name several pages.

The goal is a shell whose commands are **applets**, like BusyBox: one type per command, carrying its own usage, options, operands and implementation; a kernel that lexes, parses and expands once; and shared tools handed to every applet rather than reinvented in each.

## The shape

```
command text
   │
   ├─ ShellLine.Lex            Core   quotes, escapes, operators → ShellWord[]
   ├─ ShellRegistry.Find       Web    name → ShellApplet
   ├─ applet.Syntax.Match      Core   flags, clustered flags, -n5/-U3, operand counts → ShellCommandLine
   ├─ ShellExpansion.Expand    Web    globs + kinds → ShellPath[]  (the tree, folders, permissions)
   ├─ applet.RunAsync          Web    given ShellCommandLine + ShellContext → ShellOutput
   └─ ShellRunner envelope     Web    { output, truncated } and the budget
```

**`uk.osric.asp.Core.Shell`** (new folder) — everything pure, no storage:

- `ShellWord` — a word as a list of `ShellSegment(string Text, bool IsLiteral)`. Quoting and escaping are *kept* rather than re-encoded: a wildcard inside a quoted segment is the character, and `Text` is the concatenation for a query or a key. This deletes today's `\*` convention.
- `ShellToken` — `ShellWord` or `ShellOperator(char)`. **The lexer no longer refuses operators**: `|`, `>`, `<`, `&`, `;` and a newline come out as tokens of their own, in order, exactly as a shell's lexer produces them. Refusing them is the *kernel's* policy for now ("one command per line; pipes and redirections are not implemented yet"), not a property of the line, so a later pipeline changes the kernel and leaves the lexer alone.
- `ShellLine.Lex(string)` — the tokeniser, moved out of `VirtualShellParser`, keeping the rules it has: `$` and a backtick refused outside single quotes, unterminated quote, trailing backslash, and quoting/escaping applied per segment. It returns tokens + an error, and nothing about commands.
- `ShellSyntax` — a command's command line, declared as data:
  ```csharp
  public sealed record ShellSyntax(
      IReadOnlyList<char> Flags,                     // -l -r -i
      IReadOnlyList<ShellValueOption> Values,        // -n <count>, -U <count>
      IReadOnlyList<ShellOperand> Operands);         // <query:text>, [path...:prefix]

  public enum ShellOperandKind { Text, Path, Prefix }

  public sealed record ShellOperand(string Name, ShellOperandKind Kind, int Minimum, int Maximum);
  ```
  `shellSyntax.Match(words)` is **the one** option/operand parser: flags may cluster (`-lR`), a value may attach (`-n5`, `-U3`) or take the next word, options must lead, and a word that does not fit is refused naming the applet's usage. Everything `VirtualShellParser.TakeOption` / `ValidArguments` / `TryTakeContext` do today, as one algorithm and no `switch` over command names.
- `ShellCommandLine` — what `Match` produced: `IReadOnlySet<char> Flags`, `IReadOnlyList<char> ValueLetters` with `int Value(char letter, int fallback)`, and `IReadOnlyList<ShellWord> Operands`.
- `PageGlob` (stays in `Core.Pages`) — rewritten to match a `ShellWord` pattern: literal segments literal, `*` and `?` only in unquoted segments.
- `GrepPattern` (stays) — unchanged; it is the model the rest follows: a command's own language in its own type (`find` gets one too, below).

**`uk.osric.asp.Web.Assistant.Shell`** (new folder) — the applets and the kernel that runs them:

- `ShellApplet` — the BusyBox applet:
  ```csharp
  internal abstract class ShellApplet {
      public abstract string Name { get; }            // "grep"
      public abstract string Usage { get; }           // "grep [-i] [-n] [-r] [-l] [-c] <query> [path ...]"
      public abstract string Description { get; }     // one line; help prints it
      public virtual string? Notes => null;           // help grep prints it: the syntax one line cannot carry
      public abstract ShellSyntax Syntax { get; }
      public abstract ValueTask<ShellResult> RunAsync(ShellCommandLine line, ShellContext context, CancellationToken cancellationToken);
  }
  ```
  Usage, description, notes, syntax and behaviour are one file. `ShellHelp` is deleted; `help` renders the registry.
- `ShellRegistry` — the applet table, constructed once (DI in `Program.cs`), `Find(name)`, `All` in ordinal order. `help` is an applet that reads it.
- `ShellRunner` — the kernel entry point `PageToolExecutor.ShellAsync` calls: lex → find → `Syntax.Match` → expand → context → `RunAsync` → envelope. It owns the result shape (`{ output, truncated }`) and the `MaxToolResultChars` budget, so no applet truncates in its own way. It also owns the *line structure* policy: today it refuses any token between commands ("one command per line; a pipe is not implemented yet"), which is the single place pipelines and redirections will change.
- `ShellContext` — the shared tools, one instance per invocation: `ShellCommandLine Line`, `IReadOnlyList<ShellPath> Paths` (already expanded and validated), `IPageStoreView Store`, `IShellOutput Output`, `IShellInput Input`.
- `IShellOutput` / `ShellOutput` — where an applet writes, rather than returning a string: `Write(string)`, `Raw(string)` (a source as it is, for `cat`), `Note(string)` (the bracketed "there is more" notes), `Truncated`, and the two limits (`MatchLimit`, `ListingLimit`) so being noisy at a limit stays one rule. The tool's implementation collects into a bounded buffer; a pipeline later supplies one that feeds the next applet's input, and no applet changes.
- `IShellInput` / `ShellInput` — what an applet may read when it has no operands. Today it is always empty (there is no stdin); applets that POSIX lets read input (`cat`, `grep`, `wc`, `head`, `tail`) ask the context for it, so the shape is already right for `|`.
- `IPageStoreView` / `PageStoreView` — the page tree as the shell sees it, one implementation over `PageEditor` + `PagePathPolicy`:
  ```csharp
  internal sealed record ShellPath(PagePath Key, bool IsFolder, bool IsPage, string Mode);

  internal interface IPageStoreView {
      Task<IReadOnlyList<ShellPath>> NodesAsync(CancellationToken ct);                     // keys and folders
      Task<IReadOnlyList<ShellPath>> ExpandAsync(ShellWord word, ShellOperandKind kind, CancellationToken ct);
      Task<ShellFile?> ReadAsync(ShellPath path, CancellationToken ct);                    // source + template, or null
  }
  ```
  `IsBelow`, `IsDirectlyBelow`, `FolderOf`, `Mode`, `NodesAsync`, `ShadowedBy` and path validation move here, out of the executor. `ShellPath` is computed once, so no applet re-derives folder-ness or permission.
- `ShellExpansion` — the stage: operands + kinds → `ShellPath`s. A pattern names every node it matches; nothing matching is a refusal (*"No key matches /vim/\*.css. ls /vim lists what is there."*); a `Path` operand that names a folder is passed on and the applet says *"is a folder, not a page"*; the *declared* `Minimum`/`Maximum` decide "grep needs a query", "diff needs a pair", "wc reads every file it is given".
- `Applets/` — one file each: `CatApplet`, `DiffApplet`, `FindApplet`, `GrepApplet`, `HeadApplet`, `HelpApplet`, `LsApplet`, `StatApplet`, `TailApplet`, `WcApplet`.

## `find`, the POSIX subset

`find` today is `ls -Rl` — an invention. It becomes a small POSIX `find`:

```
find [path ...] [-name pattern] [-type f|d] [-path pattern]
```

- Operands are paths (default: the root), and the test is the conjunction of the predicates given. `-name` matches the **last segment** (POSIX `fnmatch` semantics, the same `PageGlob` language, quoting honoured), `-path` matches the whole key, `-type` takes `f` for a page or shell file and `d` for a folder. No `!`, no `-o`, no `-size`/`-mtime`/`-newer` yet: a documented subset, refused by name when used.
- It prints matching **keys**, one per line, as POSIX `find` prints paths — no mode column, because that was `ls -Rl`'s job.
- The predicate expression is its own type, `FindExpression` (in the applet's file), parsed from the words `ShellSyntax` hands it — the way `GrepPattern` is `grep`'s language. Flags that take a value (`-name`, `-path`) cannot be `ShellSyntax` flags, so `FindApplet` parses its own tail after the paths; `ShellSyntax` gives it the operands and it owns the rest. This is the pattern for any future command with an expression (`awk`-like, `sed`-like).

## The applets

Each row is one file with its usage, syntax, notes and behaviour together. `POSIX` is the reference for options and output.

| Applet | Usage | Notes on the change |
| --- | --- | --- |
| `ls` | `ls [-l] [-R] [-r] [path ...]` | flags cluster (`-lR`); `-r` reverses, `-R` descends; a path that is a folder lists its contents, one that is a page names the page |
| `find` | `find [path ...] [-name …] [-type …] [-path …]` | POSIX subset above; prints keys |
| `cat` | `cat [-n] <key> [key ...]` | several files; `-n` numbers each file from one, because a line number names a line of a page |
| `head` / `tail` | `head [-n <count>] [key ...]` | POSIX multi-file output (`==> key <==` headers); default ten; `-n5` and `-n 5` |
| `wc` | `wc [-l] [key ...]` | POSIX multi-file output (`count key` lines and a `total`); counts lines |
| `grep` | `grep [-i] [-n] [-r] [-l] [-c] <query> [path ...]` | unchanged behaviour; the query stays `GrepPattern` |
| `stat` | `stat <key> [key ...]` | one record per key |
| `diff` | `diff [-b] [-u \| -U <count>] <key> <key>` | unchanged output; `-U3` attaches; `identical` moves out of the envelope and into POSIX's exit status (= `ok`) |
| `help` | `help [command]` | rendered from the registry, so it cannot drift |

Behaviour changes for the model, deliberately: patterns are back (as a stage, quoting honoured); `find` is POSIX and prints keys; flags cluster and values attach; `head`/`tail`/`wc` take several files in POSIX's format; `diff`'s result is `ok` + empty output when there is no difference, and `help diff` says so; and a pipe or a redirect is refused as *not implemented yet* rather than as impossible, because operators are the next thing this kernel will grow.

## Later: operators

Pipes and redirections are not in this plan, but two of its shapes exist so that adding them is a kernel change and nothing else:

- **The lexer already emits operators as tokens**, so a `ShellLine` is a token list, not a single command. A pipeline is then a kernel stage that splits the tokens on `|`, builds one `ShellContext` per segment and connects each segment's `IShellOutput` to the next segment's `IShellInput`.
- **Applets write to an output and read an input**, not to a string. Nothing in an applet knows whether its output is the tool result or the next command's stdin.
- **What `>` will need:** a redirect target is a *write* to a page, and the shell tool is read-only today (`NeedsApproval: false`). A redirection that writes must go through the same path policy and approval as `write_page`, or be refused; that is a security decision, not an applet's, and it belongs with the operators rather than here.
- **Standard input changes one documented deviation.** Today a path-less `grep`/`cat`/`wc`/`head`/`tail` reads the store (the root level), because there is no stdin and "search nothing" would be useless. POSIX says they read standard input. When `|` lands the applets will read `IShellInput` when it has content, and the path-less case becomes a decision to take then: keep the store default (useful, and a deliberate deviation of a shell built for a page store, like `ls` on a page naming the page) or follow POSIX and read an empty input. The deviation is recorded in DEC-226 either way, so it is visible rather than accidental.



## Migration

Six commits, `make check` green at each, so the branch is never broken.

0. **Park the glob plumbing, keep the matcher.** Commit `src/uk.osric.asp.Core/Pages/PageGlob.cs` and `test/uk.osric.asp.Core.Tests/PageGlobTests.cs` (pure, correct, and it survives: it only learns to take a `ShellWord` when expansion lands in phase 4). Stash the rest of the uncommitted glob change — the `ExpandAsync` plumbing, the parser's wildcard escaping, the `ShellHelp` preface, the executor's scope lists and the tests that go with them — and delete the one failing test. Net tree: the diff work of `e837328`, plus the matcher.
1. **The lexer.** Add `Core/Shell/ShellLine.cs`, `ShellToken`, `ShellWord`, `ShellSegment` and `ShellLineTests` (the quoting, escaping and expansion-refusal cases now in `VirtualShellTests`, moved and extended with operators as tokens). `VirtualShellParser.Parse` delegates to it and keeps refusing operators for now, so behaviour is identical and the existing suite proves it.
2. **The syntax.** Add `ShellSyntax`/`ShellCommandLine`/`ShellSyntaxTests`. Replace `TakeOption`, `ValidArguments`, `TryTakeContext` and the dash guards with ten `ShellSyntax` declarations (still in one file at this point) and `Match`. Clustered flags and attached values come in here, with a test per declared form; `VirtualShellTests` shrinks to the lexer's cases.
3. **The kernel and the first applets.** Add `ShellApplet`, `ShellRegistry`, `ShellRunner`, `ShellContext`, `ShellOutput`, `IPageStoreView`, `PageStoreView`, `ShellPath`, and port `help`, `ls`, `cat`, `stat` — the applets that only read. `PageToolExecutor.ShellAsync` delegates to `ShellRunner` for those four and keeps the old path for the rest. `ShellHelp.cs` becomes `HelpApplet`; `ShellHelpTests` becomes `ShellRegistryTests`.
4. **Expansion, then the rest.** Add `ShellExpansion` and bring globs back as the stage. Port `grep`, `wc`, `head`, `tail` (with the POSIX multi-file output), then delete `GrepAsync`, `ReadAsync`, `CountOrNameAsync`, `KeysMatching`, `CountsMatching`, `SourcesAsync`, `IsInScope`, `ResolveAsync`, `LineCount`, `WithinBudget` and the rest of the executor's shell helpers. `find` is rewritten as the POSIX subset here, as a new `FindApplet` + `FindExpression`.
5. **`diff` last, and the cleanup.** Port `DiffApplet`; delete `PageToolExecutor`'s last shell members, `ShellHelp`, the parser's tables and the now-dead `VirtualShellOptions`; move the shell tests out of `PageToolExecutorTests` into per-applet fixtures. `PageToolExecutor` ends at roughly the page tools' size, and `ShellAsync` is three lines.
6. **Docs.** `docs/plans/assistant-shell.md` is this plan (DEC-09: a plan is written before a large change and kept). `docs/decisions.md` gains DEC-226 (the applet architecture: a command is a type carrying its own usage, syntax and behaviour; the kernel lexes, parses and expands once; help is rendered from the registry), and DEC-227 (the `find` subset); DEC-224's POSIX rule is cited by both. `docs/assistant.md` gets one sentence on applets; `docs/pages.md` needs nothing.

## Verification

Per phase, `make check` (format, licences, package audit, four suites, Release publish).

New and moved tests:

- `ShellLineTests` (Core) — quotes of both kinds, escapes, unmatched quote, trailing backslash, operators, `$`/backtick, glob-ness of quoted vs unquoted words.
- `ShellSyntaxTests` (Core) — each declared form accepted (including `-lR`, `-n5`, `-U3`, options before operands), each refused (unknown flag, missing value, trailing option, too few/too many operands), for one syntax per applet shape.
- `PageGlobTests` (Core) — the existing table, rewritten against `ShellWord`, plus the quoted-wildcard cases.
- `GrepPatternTests`, `NormalDiffTests`, `DiffOptionTests`, `LineDiffTests`, `UnifiedDiffTests` — unchanged.
- One fixture per applet (Web): `CatAppletTests`, `LsAppletTests`, `FindAppletTests` (each predicate and the conjunction), `GrepAppletTests`, `HeadAppletTests`, `TailAppletTests`, `WcAppletTests`, `StatAppletTests`, `DiffAppletTests`, `HelpAppletTests` — over a fake `IPageStoreView`.
- `ShellRegistryTests` (Web) — invariants that make drift impossible rather than caught: every applet's `Usage` starts with its name; every `-x` its usage mentions is declared in its syntax and every declared flag appears in its usage; `help` lists exactly the registry in ordinal order; each applet has a one-line description ending in a full stop.
- `ShellRunnerTests` (Web) — the envelope (`output`, `truncated`), the budget, the limits note, and that an unknown command names itself and points at `help`.
- `PageToolExecutorTests` keeps a dozen end-to-end shell cases (the tool's JSON surface), not 47.

## Risks and non-goals

- **Behaviour regresses while it moves.** The guard is that the existing suite moves applet by applet inside the same commits, and the gate runs each time.
- **`Core` must stay pure** (DEC-10): the lexer, syntax, glob and pattern live there; the applets read storage, so they live in `Web` behind `IPageStoreView`.
- **What stays in `PageToolExecutor`:** the page tools (`list_pages`, `read_page`, `edit_page`, `write_page`, `delete_page`, `search_pages`, `check_script`, `check_script_text`, `fetch`, the memory tools, `diff_page`) and the helpers they use — `EntriesAsync`/`Entry`, `FindMatchesAsync` for `search_pages`, `Mode(PagePath)`, `ShellRoutes`' records, `RefuseBeforeApproval`. The shell stops borrowing them; where both want one (the ordinal key scan for `search_pages` and `grep`), the shell's copy goes behind `PageStoreView` and `search_pages` keeps its own.
- **Not in this plan:** process execution, a host filesystem, `cd`/relative paths, `**`, brace expansion, and POSIX commands we have not got (`sed`, `awk`, `sort`, `uniq`). `find`'s expression is a subset by design, and an unsupported predicate is refused by name.
- **Cost:** ~10 new applet files, ~8 new Core/kernel types, ~10 deleted or emptied, 6 commits. The tool's JSON surface is unchanged except that `diff` loses `identical` in favour of `ok` and empty output.
