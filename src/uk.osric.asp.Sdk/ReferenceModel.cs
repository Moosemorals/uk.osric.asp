// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The documentation one build carries, as the reference tool writes it.</summary>
/// <remarks>
/// The corpus is built once, when the assemblies are, and read by both the page and the assistant's
/// tool. It holds no presentation: a declaration is a string and the tokens it is made of, a doc
/// block is a small tree, and the renderer decides what markup that becomes.
/// </remarks>
/// <param name="Slug">The URL segment that names the assembly.</param>
/// <param name="Assembly">The assembly the documentation describes.</param>
/// <param name="Title">The heading a reader sees.</param>
/// <param name="Description">One line: what the assembly is for.</param>
/// <param name="TypeCount">How many type sections the corpus holds.</param>
/// <param name="EntryCount">How many documented entries the source file carried.</param>
/// <param name="Namespaces">The namespaces, in reading order.</param>
public sealed record ReferenceCorpus(
    string Slug,
    string Assembly,
    string Title,
    string Description,
    int TypeCount,
    int EntryCount,
    IReadOnlyList<ReferenceNamespace> Namespaces);

/// <summary>One namespace and the types it holds.</summary>
/// <param name="Name">The namespace, or the empty string for the global namespace.</param>
/// <param name="TypeCount">How many types the namespace holds.</param>
/// <param name="Types">The types, in reading order.</param>
public sealed record ReferenceNamespace(
    string Name,
    int TypeCount,
    IReadOnlyList<ReferenceType> Types);

/// <summary>One documented type.</summary>
/// <param name="Id">The raw documentation id.</param>
/// <param name="Anchor">The URL fragment its section carries.</param>
/// <param name="Name">Its name, with a nested type's outer names.</param>
/// <param name="Display">Its name with its type parameters.</param>
/// <param name="Kind">The word for its kind: <c>class</c>, <c>interface</c>, <c>enum</c>, and so on.</param>
/// <param name="Declaration">Its declaration, split into coloured tokens.</param>
/// <param name="Summary">Its summary block.</param>
/// <param name="Remarks">Its remarks block.</param>
/// <param name="TypeParameters">Its type parameters and their text.</param>
/// <param name="Inherited">Where its documentation was inherited from, when it was.</param>
/// <param name="Members">Its documented members.</param>
public sealed record ReferenceType(
    string Id,
    string Anchor,
    string Name,
    string Display,
    string Kind,
    ReferenceDeclaration Declaration,
    ReferenceDoc Summary,
    ReferenceDoc Remarks,
    IReadOnlyList<ReferenceParameter> TypeParameters,
    ReferenceInherited? Inherited,
    IReadOnlyList<ReferenceMember> Members);

/// <summary>One documented member of a type.</summary>
/// <param name="Id">The raw documentation id.</param>
/// <param name="Anchor">The URL fragment its section carries.</param>
/// <param name="Kind">The word for its kind: <c>Method</c>, <c>Property</c>, <c>Field</c>, and so on.</param>
/// <param name="Display">Its signature, as the anchor text names it.</param>
/// <param name="Declaration">Its declaration, split into coloured tokens.</param>
/// <param name="Summary">Its summary block.</param>
/// <param name="Remarks">Its remarks block.</param>
/// <param name="TypeParameters">Its own type parameters and their text.</param>
/// <param name="Parameters">Its parameters and their text.</param>
/// <param name="Returns">Its returns block.</param>
/// <param name="Value">Its value block.</param>
/// <param name="Exceptions">What it throws.</param>
/// <param name="Inherited">Where its documentation was inherited from, when it was.</param>
public sealed record ReferenceMember(
    string Id,
    string Anchor,
    string Kind,
    string Display,
    ReferenceDeclaration Declaration,
    ReferenceDoc Summary,
    ReferenceDoc Remarks,
    IReadOnlyList<ReferenceParameter> TypeParameters,
    IReadOnlyList<ReferenceParameter> Parameters,
    ReferenceDoc Returns,
    ReferenceDoc Value,
    IReadOnlyList<ReferenceExceptionDoc> Exceptions,
    ReferenceInherited? Inherited);

/// <summary>A declaration as text and as the tokens a renderer colours.</summary>
/// <param name="Text">The declaration, exactly as it reads.</param>
/// <param name="Tokens">The same text in coloured runs.</param>
public sealed record ReferenceDeclaration(string Text, IReadOnlyList<ReferenceToken> Tokens);

/// <summary>One run of a declaration.</summary>
/// <param name="Kind">The run's colour: <c>keyword</c>, <c>type</c>, <c>member</c>, and so on.</param>
/// <param name="Text">The run's text.</param>
/// <param name="Href">Where the run links, when it names something the corpus or the API browser explains.</param>
public sealed record ReferenceToken(string Kind, string Text, string? Href = null);

/// <summary>A documentation block: its plain text and the tree a renderer walks.</summary>
/// <param name="Text">The block flattened to running text.</param>
/// <param name="Nodes">The block as a small tree of paragraphs, lists, links and inline spans.</param>
public sealed record ReferenceDoc(string Text, IReadOnlyList<ReferenceNode> Nodes) {

    /// <summary>A block with no text and no nodes.</summary>
    public static ReferenceDoc Empty { get; } = new(string.Empty, []);
}

/// <summary>One node of a documentation block.</summary>
/// <param name="Kind">
/// What the node is: <c>text</c>, <c>para</c>, <c>code</c>, <c>pre</c>, <c>link</c>, <c>strong</c>,
/// <c>em</c>, <c>br</c>, <c>list</c>, <c>item</c>, <c>term</c>, <c>description</c>, <c>table</c>,
/// <c>row</c>, <c>cell</c> or <c>header</c>.
/// </param>
/// <param name="Text">The text of a <c>text</c>, <c>code</c> or <c>pre</c> node.</param>
/// <param name="Href">The target of a <c>link</c>.</param>
/// <param name="Ordered">Whether a <c>list</c> is numbered rather than bulleted.</param>
/// <param name="Children">The node's children.</param>
public sealed record ReferenceNode(
    string Kind,
    string? Text = null,
    string? Href = null,
    bool Ordered = false,
    IReadOnlyList<ReferenceNode>? Children = null);

/// <summary>A named parameter or type parameter and its text.</summary>
/// <param name="Name">The name the documentation gives it.</param>
/// <param name="Doc">Its text.</param>
public sealed record ReferenceParameter(string Name, ReferenceDoc Doc);

/// <summary>One exception a member documents.</summary>
/// <param name="Name">The exception's short name.</param>
/// <param name="Href">Where its documentation lives, when the link can be computed.</param>
/// <param name="Doc">Its text.</param>
public sealed record ReferenceExceptionDoc(string Name, string? Href, ReferenceDoc Doc);

/// <summary>Where an entry's documentation was inherited from.</summary>
/// <param name="From">The source id, or null when no source could be named.</param>
/// <param name="Display">The source as a reader sees it.</param>
/// <param name="Href">The source's page, or its page on Microsoft Learn.</param>
/// <param name="Unknown">Whether the entry only says it inherits its documentation.</param>
public sealed record ReferenceInherited(string? From, string? Display, string? Href, bool Unknown);
