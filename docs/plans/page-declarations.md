<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->

# Declarations inside a page

Implementation plan for the first phase of [IDEA-005](../jobs/IDEA-005.md): a page may declare
`using` directives and types of its own, and use them from its code blocks. The job's second
question — types shared *between* pages — is deliberately not here. It needs a dependency order and
an invalidation story of its own, and is easier to plan once this phase has been written against
real pages.

## Status

Not started. Decisions settled 2026-11-04.

## Goal and constraints

- A template can hold one or more `<%! %>` blocks containing `using` directives and type
  declarations, reference them from `<% %>` blocks, and save and serve like any other page.
- No storage, schema, migration, dependency or `RuntimeId` change. The generator already changes
  when `Core` changes, so every stored image is invalidated by the usual module version id.
- The page API policy still governs page-declared types. A `class` in a page must not become a way
  to reach `System.IO`, reflection or the shell.
- `PAGE002` still holds its original line: a page cannot escape `Handle` to add statics, an entry
  point or a module initialiser of its own.
- Generation stays in `Core`, reference-free and Roslyn-free. The editor's "Generated C#" view and
  the compiler must see the same source.

## The short version

1. The template language gains one opener, `<%! %>`, for a block of namespace-level declarations.
2. `TemplateTokenizer` emits a third token kind, `Declaration`, and reuses `TEMPLATE001` for an
   unterminated block, naming the `<%!` the author has to close.
3. `TemplateSourceGenerator` emits every declaration block, in template order, between the
   generated `using` and the generated handler class, each still wrapped in its own `#line`.
4. `PageApiChecker.CheckShape` relaxes from "exactly one class" to "the generated handler, last,
   plus the page's own type declarations".
5. The API walk is unchanged: once declarations are in the tree, `IsDeclaredIn`, `Collects` and
   `PageApiPolicy.Check` already apply to them.
6. `CompiledPage` finds `GeneratedPage` by name rather than by "first `IHandler`", because a page
   can now declare a type that implements the interface.

## A page

```text
<%!
using System.Globalization;

sealed class Amount {
    public static string Format(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);
}
%>
<p>Total: <% ctx.Write(Amount.Format(42.5m)); %></p>
```

## Decisions

### D1 — The opener is `<%! %>`, not `<%+ %>` and not hoisting by inspection

`<%!` is the established spelling for a declaration block and reads as "declaration"; `<%+` reads as
"append". Hoisting a code block *because* it begins with `using` or `class` is invisible to the
author and is guesswork about text, which is why the idea already prefers a delimiter.

Tokenising is a two-character lookahead on the existing opener: after `IndexOf("<%")`, a `!` at
`codeStart + 2` makes the block a declaration and content starts at `codeStart + 3`. There is no
ambiguity with code, because a C# statement cannot begin with `!`; the rule is simply that the `!`
must be adjacent to the opener.

### D2 — Declaration blocks are hoisted above the handler, in template order

`TemplateSourceGenerator` becomes a two-pass emitter:

1. `using uk.osric.asp.Sdk;`
2. every `Declaration` token, in template order, each with its own `#line … "page"`, column padding
   and `#line default`;
3. the generated class and the `Handle` prelude;
4. every `Text` and `Code` token exactly as today.

Declarations are compile-time only, so hoisting changes nothing about the body's output order.
`<%! def1 %>`, then text, then `<%! def2 %>` generates `def1`, then `def2`, then the class, and the
text still writes where it sits. Both declarations are visible to the whole body, including code
that appears above its block: C# declarations are not sequential. A declaration block is never
conditional, even when it sits between `<% if (…) { %>` and `<% } %>`.

`using` directives must precede type declarations in a compilation unit, and the generator does not
parse C#, so template order is also the using order. A `<%! using … %>` block placed after a
`<%! class … %>` block is reported by Roslyn as `CS1529` at the using's own template line and
column. That is the whole ordering rule, and it is one sentence in the documentation.

The alternative — sorting blocks that contain only `using` directives to the front — was
considered and rejected. It would need a text heuristic around comments, `#if` and multi-line
aliases to be useful, and it hides which block came first for no gain once the rule is written
down.

### D3 — The generated handler names SDK types fully qualified

The prelude becomes:

```csharp
using uk.osric.asp.Sdk;

public class GeneratedPage : uk.osric.asp.Sdk.IHandler {
public void Handle(uk.osric.asp.Sdk.IContext ctx) {
```

The generated `using` stays for the page's own code, but the generated signature names the SDK
explicitly. Once a page may declare global types, a page type called `IContext` or `IHandler` would
otherwise shadow the imported names and break the *generated* signature — an unmapped Roslyn error,
which is a `500` for an author's naming choice. Qualifying the signature removes the whole class of
foot-guns for one line of change.

### D4 — One new token kind, no new diagnostic code

`TokenType` gains `Declaration`. `Token`'s documentation says a token is text, code or a declaration
block. An unterminated `<%!` reuses `TEMPLATE001` with a message naming the opener:

```text
TEMPLATE001: unterminated declaration block; the '<%!' has no matching '%>'.
```

The code means "a template block has no matching close", and the message distinguishes the opener,
so the "codes are handed out once" rule is kept without inventing `TEMPLATE002`.

### D5 — `PAGE002` is now "one handler, last, plus type declarations"

`CheckShape` changes from "one class with one method" to:

- the **last** top-level member is a class named `GeneratedPage` whose only member is a method named
  `Handle` (the generator always emits its class last);
- every member before it is a `BaseTypeDeclarationSyntax` (class, struct, interface, enum, record)
  or a `DelegateDeclarationSyntax` — no namespace or file-scoped namespace, no top-level statements;
- no page-declared type is named `GeneratedPage`;
- the compilation unit has no assembly-level attributes.

Anything else is `PAGE002`. The messages are specific where the problem is specific:

| Problem | Message |
| --- | --- |
| the handler did not survive, or gained members | `PAGE002: page code must stay inside a code block; a declaration block may only declare types.` |
| a declaration block declares a namespace | `PAGE002: a page may not declare a namespace.` |
| a page declares `GeneratedPage` | `PAGE002: a page may not declare its own GeneratedPage.` |
| a declaration block adds an assembly attribute | `PAGE002: a page may not add assembly attributes.` |

Members *inside* a page-declared type are deliberately allowed. The API walk checks them, which is
what keeps the original guarantee: no statics on the handler, no entry point, no module initialiser.

### D6 — `PAGE002` carries a template position when it has one

Shape diagnostics are currently unpositioned. Declaration blocks are inside `#line` regions, so the
offending node usually has a mapped span. Reuse `TemplatePositionOf` and report it; the position
stays `null` only for a fault in generated code. Existing tests assert only the `PAGE002:` prefix,
so nothing needs relaxing.

### D7 — The entry point is looked up by name

`CompiledPage` currently takes the first type assignable to `IHandler`. Page declarations are
emitted before `GeneratedPage`, so a page's own helper could become "first" and be invoked instead
of the handler. Replace the search with `assembly.GetType("GeneratedPage")`, keeping the existing
"No handler found in the assembly." fault when it is `null`.

### D8 — The API policy applies to declarations with no allow-list change

The existing walk covers declarations as soon as they are in the tree:

- `IsDeclaredIn` treats a page-declared type as page code, so using a helper needs no permission;
- every type, member, base type and attribute a declaration names goes through
  `PageApiPolicy.Check`, so `<%! class H { System.IO.FileInfo f; } %>` is `PAGE001` at the
  declaration's template position rather than a compiler fault;
- `FindBanned` still covers `stackalloc`, pointers, function pointers, `unsafe` blocks and
  `dynamic` inside a declared type;
- a `using` directive grants no capability by itself — `Console.WriteLine` still resolves to
  `System.Console` and is refused — and an alias is resolved through `IAliasSymbol` before the
  policy sees it.

No allow-listed type or namespace is added.

### D9 — No version bump, and static state is per loaded image

`PolicyVersion` does not move: no stored image could have contained declarations under the old
rules, so nothing stale can be served. `FormatVersion` does not move either: the generator lives in
`Core`, whose module version id is already part of `RuntimeId`, so the deploy that ships this
invalidates every stored image.

`PageRenderer` loads an image per request, so a `static` field in a page-declared type lives for one
request. That is a property to document, not a feature to build on: structure now, persistence is
[IDEA-003](../jobs/IDEA-003.md).

## Implementation sequence

Test-first throughout; one commit per step.

### Step 1 — tokenizer

In `Core`, add `TokenType.Declaration` and the lookahead in `TemplateTokenizer`, then the cases in
`TemplateTokenizerTests`:

- `ADeclarationBlockIsADeclarationToken` — content without delimiters, position at the first content
  character;
- `ADeclarationBlockPreservesItsContent` — whitespace and newlines kept, like a code block;
- `AnUnterminatedDeclarationBlockIsReportedAtItsOpeningDelimiter` — position of `<%!`, message names
  `'<%!'`;
- `AnExclamationIsADeclarationOnlyWhenAdjacentToTheOpener` — `<% !value %>` stays code;
- every existing case stays green, including `text %> more text` and `<%<%nested%>`.

### Step 2 — generator

In `Core`, split the emitter into "mapped token" and "body token" passes and qualify the generated
signature, then the cases in `TemplateSourceGeneratorTests`:

- `DeclarationsAreEmittedBeforeTheHandler`;
- `DeclarationBlocksAreMappedToTheirTemplateLineAndColumn` — `#line`, padding and `#line default`;
- `DeclarationBlocksDoNotWriteOutput` — no `WriteRaw` for their content;
- `MultipleDeclarationBlocksKeepTheirOrder`;
- `TextAroundDeclarationBlocksKeepsItsOutputOrder`;
- `TheHandlerNamesSdkTypesFullyQualified`.

Assert ordering and mapping rather than whole-source strings, so the prelude stays cheap to change.

### Step 3 — shape rule

In `Runtime`, rewrite `CheckShape` and position its diagnostics, then the cases in
`PageApiPolicyTests`:

- `ADeclarationBlockMayDeclareAType`;
- `AStatementInADeclarationBlockIsRejected` — `PAGE002`, positioned;
- `ANamespaceInADeclarationBlockIsRejected` — `PAGE002`, positioned;
- `APageMayNotDeclareGeneratedPage` — `PAGE002`;
- `AnAssemblyAttributeIsRejected` — `PAGE002`;
- the existing `DeclaringMembersIsRejected` cases stay green.

### Step 4 — policy inside declarations

In `Runtime`, new cases beside the existing policy tests:

- `ABannedApiInsideADeclaredTypeIsRejectedAtItsTemplatePosition` — `System.IO.File.ReadAllText` in a
  helper method is `PAGE001` with a `Position` on the declaration;
- `AUsingDirectiveDoesNotBypassThePolicy` — `using System.IO;` plus `File.ReadAllText` is `PAGE001`;
- `AUsingBlockAfterATypeBlockIsReportedInTemplateSpace` — `CS1529` at the using's template line;
- `APageDeclaredTypeMayUseAnAllowedBclType` — `StringBuilder`, `List<int>`, `CultureInfo`;
- `AnErrorInsideADeclaredTypeReportsItsTemplateLineAndColumn`;
- `ADeclarationBlockThatOnlyImportsANamespaceCompiles`.

### Step 5 — compile, load and run

In `Runtime`, the handler lookup change in `CompiledPage`, then the cases in `PageCompilerTests`:

- `APageDeclaredTypeIsUsableFromPageCode` — compile, load, invoke, assert the output;
- `APageDeclaredTypeIsUsableFromMoreThanOneBlock`;
- `APageDeclaredHandlerIsNotTheEntryPoint` — a decoy `IHandler` declared before the handler does not
  write the output;
- `AnUnmappedDeclarationPolicyViolationThrowsInsteadOfReturningADiagnostic` — mirrors the existing
  unmapped-fault cases.

### Step 6 — documentation

- `README.md`, "A page": a short `<%! %>` example and the ordering sentence.
- `docs/architecture.md`: the template description, the diagnostics table (`TEMPLATE001` wording,
  `PAGE002` meaning), and the "Page API policy" shape-rule paragraph.
- `docs/decisions.md`: add a decision for "declarations are `<%! %>`, hoisted in template order" and
  move `DEC-36` to **Superseded**, as that file's own rule requires.
- `docs/jobs/IDEA-005.md`: a `## Status` section recording that phase 1 is done and phase 2 is open,
  in the style of [SEC-03](../jobs/SEC-03.md).
- Fill this plan's status with the test counts after `make check`.

## Edge cases, and what happens

| Template | Result |
| --- | --- |
| `<%! class H { } %>` | hoisted; `H` usable anywhere in the body |
| `<%! class A { } %><%! class B { } %>` | both hoisted, A then B |
| `<%! using System.Text; %>` before any type | compiled, using applies to the whole file |
| a using block after a type block | `CS1529` at the using's template position |
| `<%! helper1 %>` … body … `<%! helper2 %>` | both hoisted; body order unchanged |
| declaration block inside `<% if … { %> … <% } %>` | always hoisted; never conditional |
| statement in a declaration block | `PAGE002`, positioned |
| `namespace N { … }` in a declaration block | `PAGE002`, positioned |
| `<%! class GeneratedPage { } %>` | `PAGE002` |
| `[assembly: …]` in a declaration block | `PAGE002` |
| `<%! class H { void F() { System.IO.File.ReadAllText("x"); } } %>` | `PAGE001`, positioned |
| `<%! using System.IO; %><%! class H { void F() { File.ReadAllText("x"); } } %>` | `PAGE001` |
| `<%!` with no `%>` | `TEMPLATE001`, at the `<%!` |
| a page type implementing `IHandler` | compiled and ignored; the generated handler runs |

## Files touched

| File | Change |
| --- | --- |
| `src/uk.osric.asp.Core/Compilation/TokenType.cs` | `Declaration` |
| `src/uk.osric.asp.Core/Compilation/Token.cs` | documentation |
| `src/uk.osric.asp.Core/Compilation/TemplateTokenizer.cs` | `<%!` detection, declaration message |
| `src/uk.osric.asp.Core/Compilation/TemplateSourceGenerator.cs` | two-pass emit, qualified prelude |
| `src/uk.osric.asp.Runtime/PageApiChecker.cs` | shape rule, positioned `PAGE002` |
| `src/uk.osric.asp.Runtime/CompiledPage.cs` | handler by name |
| `test/uk.osric.asp.Core.Tests/TemplateTokenizerTests.cs` | declaration cases |
| `test/uk.osric.asp.Core.Tests/TemplateSourceGeneratorTests.cs` | hoisting and signature cases |
| `test/uk.osric.asp.Runtime.Tests/PageApiPolicyTests.cs` | shape and policy cases |
| `test/uk.osric.asp.Runtime.Tests/PageCompilerTests.cs` | compile, load and run cases |
| `README.md`, `docs/architecture.md`, `docs/decisions.md`, `docs/jobs/IDEA-005.md` | documentation |

No changes to storage, migrations, `PageApiPolicy`, `PageCompiler`, `RuntimeId`, the SDK or the
editor endpoint.

## Acceptance criteria

- A page can declare `using` directives and one or more types in `<%! %>` blocks and use them from
  `<% %>`, saving and serving normally.
- A compile error inside a declared type reports the author's template line and column.
- A banned API inside a declared type is `PAGE001` with a template position, never an unmapped
  compiler fault.
- A page cannot declare a namespace, an assembly attribute, its own `GeneratedPage`, a top-level
  statement, or a member outside a type: each is `PAGE002`.
- A page-declared type implementing `IHandler` is never invoked.
- Pages without declaration blocks generate the same source shape as before, apart from the
  fully-qualified handler signature; all existing tests pass with only those assertions updated.
- `make check` is green, and this plan records the new test counts.

## Not in this phase

- Types shared between pages: a library page compiled into its dependents, a shared assembly in the
  `PageLoadContext`, or an expanded SDK all remain open in [IDEA-005](../jobs/IDEA-005.md).
- Moving `PageLayout` into a page. That needs shared types, and is the point of phase 2.
- Editor syntax highlighting for `<%!`.
- Page persistence and state; static fields are per loaded image, as in D9.
