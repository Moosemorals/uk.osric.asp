// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Compilation;

/// <summary>One API a page referenced, as plain data.</summary>
/// <remarks>
/// The runtime projects Roslyn symbols onto these values, so the policy itself stays a pure function
/// over strings and never sees a compiler type.
/// </remarks>
/// <param name="Namespace">The namespace the API lives in, for example <c>System.IO</c>.</param>
/// <param name="Type">The type name without its namespace, for example <c>File</c>.</param>
/// <param name="Member">The member used, or <see cref="string.Empty"/> for a plain type reference.</param>
/// <param name="Position">Where the reference appears in template space, or null when it is generated code.</param>
public readonly record struct ApiReference(string Namespace, string Type, string Member, TemplatePosition? Position);
