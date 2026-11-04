// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Text;

/// <summary>One element read out of a rendered document, with its attributes and its nesting.</summary>
internal sealed class MarkupElement {

    private readonly List<MarkupElement> _children = [];
    private readonly Dictionary<string, string> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private readonly StringBuilder _text = new();

    internal MarkupElement(string name, MarkupElement? parent = null) {
        Name = name;
        Parent = parent;
    }

    /// <summary>The element's name, lower-cased.</summary>
    public string Name { get; }

    /// <summary>The element that encloses this one, or null for the document itself.</summary>
    public MarkupElement? Parent { get; }

    /// <summary>The attributes, matched case-insensitively, with their values decoded.</summary>
    public IReadOnlyDictionary<string, string> Attributes => _attributes;

    public IReadOnlyList<MarkupElement> Children => _children;

    /// <summary>Every piece of text inside the element, at any depth.</summary>
    public string Text => _text.ToString();

    public string? Attribute(string name) => _attributes.GetValueOrDefault(name);

    public bool HasAttribute(string name) => _attributes.ContainsKey(name);

    public bool HasClass(string name) =>
        Attribute("class")?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal) == true;

    /// <summary>The element and everything inside it, in document order.</summary>
    public IEnumerable<MarkupElement> SelfAndDescendants() {
        yield return this;
        foreach (MarkupElement descendant in Descendants()) {
            yield return descendant;
        }
    }

    /// <summary>Everything inside the element, in document order.</summary>
    public IEnumerable<MarkupElement> Descendants() {
        foreach (MarkupElement child in _children) {
            yield return child;
            foreach (MarkupElement descendant in child.Descendants()) {
                yield return descendant;
            }
        }
    }

    /// <summary>Whether an element with this name encloses the element.</summary>
    public bool IsInside(string name) {
        for (MarkupElement? ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent) {
            if (string.Equals(ancestor.Name, name, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    internal void Add(MarkupElement child) => _children.Add(child);

    internal void Set(string name, string value) => _attributes[name] = value;

    internal void AppendText(string text) => _text.Append(text);
}
