// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using NUnit.Framework;
using uk.osric.asp.Core.Compilation;

/// <summary>Generates page source for a fixture, failing when the template does not parse.</summary>
internal static class TestTemplates {

    /// <summary>Generates the handler source a template compiles to.</summary>
    /// <param name="template">The template to generate from.</param>
    /// <returns>The generated source.</returns>
    public static GeneratedSource Generate(string template) {
        TemplateGenerationResult parsed = TemplateSourceGenerator.Generate(template);
        Assert.That(parsed.Source, Is.Not.Null, string.Join(Environment.NewLine, parsed.Errors));

        return parsed.Source!;
    }
}
