// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>One product assembly whose documentation this build carries.</summary>
/// <param name="Slug">The URL segment that names it under <c>/admin/reference</c>.</param>
/// <param name="AssemblyName">The assembly, which is also the XML file's name and its root namespace.</param>
/// <param name="Title">The heading a reader sees.</param>
/// <param name="Description">One line: what the assembly is for.</param>
public sealed record ReferenceAssemblyInfo(string Slug, string AssemblyName, string Title, string Description) {

    /// <summary>The four assemblies, in reading order: the contract first, then the host's layers.</summary>
    public static IReadOnlyList<ReferenceAssemblyInfo> All { get; } = [
        new("sdk", "uk.osric.asp.Sdk", "The page contract",
            "What a stored page may name and use."),
        new("core", "uk.osric.asp.Core", "The host's pure half",
            "The template language, diagnostics, the API policy and the chart helpers."),
        new("runtime", "uk.osric.asp.Runtime", "The page compiler",
            "Emitting, loading and running a page."),
        new("web", "uk.osric.asp.Web", "The site",
            "Endpoints, hosting, storage and configuration."),
    ];
}
