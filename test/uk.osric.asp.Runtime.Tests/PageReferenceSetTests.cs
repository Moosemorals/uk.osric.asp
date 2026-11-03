// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;

[TestFixture]
public sealed class PageReferenceSetTests {

    private static readonly string[] s_blankValues = [
        string.Empty,
        "   ",
        new(Path.PathSeparator, 3),
    ];

    [Test]
    public void TheRuntimeProvidesThePageReferenceSet() {
        string?[] displays = [.. PageReferenceSet.ForRuntime().Select(reference => reference.Display)];

        Assert.That(displays, Is.Not.Empty);
        Assert.That(displays, Has.Some.Matches<string?>(
            display => display is not null && display.EndsWith("System.Private.CoreLib.dll", StringComparison.Ordinal)));
        Assert.That(displays, Has.Some.EqualTo(typeof(IHandler).Assembly.Location));
    }

    [Test]
    public void MissingPlatformAssembliesIsFatal() {
        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => PageReferenceSet.ReferencedPaths(null));

        Assert.That(failure?.Message, Does.Contain("TRUSTED_PLATFORM_ASSEMBLIES"));
    }

    [TestCaseSource(nameof(s_blankValues))]
    public void BlankPlatformAssembliesIsFatal(string trustedPlatformAssemblies) {
        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => PageReferenceSet.ReferencedPaths(trustedPlatformAssemblies));

        Assert.That(failure?.Message, Does.Contain("TRUSTED_PLATFORM_ASSEMBLIES"));
    }

    [Test]
    public void AReferenceSetWithoutTheCoreLibraryIsFatal() {
        string onlyOne = Path.Combine(Path.GetTempPath(), "System.Runtime.dll");

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => PageReferenceSet.ReferencedPaths(onlyOne));

        Assert.That(failure?.Message, Does.Contain("System.Private.CoreLib"));
    }

    [Test]
    public void PlatformAssembliesDropTheShellAndRoslyn() {
        string root = Path.GetTempPath();
        string[] paths = [
            Path.Combine(root, "System.Runtime.dll"),
            Path.Combine(root, "System.Private.CoreLib.dll"),
            Path.Combine(root, "Microsoft.CodeAnalysis.dll"),
            Path.Combine(root, "uk.osric.asp.Web.dll"),
        ];

        IReadOnlyList<string> referenced = PageReferenceSet.ReferencedPaths(string.Join(Path.PathSeparator, paths));

        string[] expected = [
            Path.Combine(root, "System.Runtime.dll"),
            Path.Combine(root, "System.Private.CoreLib.dll"),
        ];

        Assert.That(referenced, Is.EqualTo(expected));
    }
}
