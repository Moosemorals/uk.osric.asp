// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using NUnit.Framework;
using uk.osric.asp.Core;

[TestFixture]
public sealed class PageApiPolicyTests {

    [TestCase("uk.osric.asp.Core", "IContext", "Write", true)]
    [TestCase("System", "String", "Concat", true)]
    [TestCase("System.Text", "StringBuilder", "Append", true)]
    [TestCase("System.Collections.Generic", "List", "Add", true)]
    [TestCase("System", "Func", "", true)]
    [TestCase("System", "InvalidOperationException", "", true)]
    [TestCase("System", "ArgumentOutOfRangeException", "", true)]
    [TestCase("System.IO", "File", "ReadAllText", false)]
    [TestCase("System", "Environment", "Exit", false)]
    [TestCase("System", "Type", "", false)]
    [TestCase("System", "Object", "GetType", false)]
    [TestCase("System.Reflection", "Assembly", "Load", false)]
    [TestCase("System.Threading", "Thread", "Sleep", false)]
    [TestCase("Microsoft.AspNetCore.Http", "HttpContext", "", false)]
    [TestCase("uk.osric.asp.Web", "SimpleContext", "", false)]
    [TestCase("uk.osric.asp.Runtime", "TemplateCompiler", "", false)]
    public void ReferenceIsAllowedAsExpected(string ns, string type, string member, bool allowed) {
        ApiReference reference = new(ns, type, member, 3);

        IReadOnlyList<string> diagnostics = PageApiPolicy.Check([reference]);

        Assert.That(diagnostics, allowed ? Is.Empty : Is.Not.Empty);
    }

    [Test]
    public void RejectionNamesTheApiAndThePageLine() {
        IReadOnlyList<string> diagnostics = PageApiPolicy.Check([new ApiReference("System.IO", "File", "ReadAllText", 12)]);

        string[] expected = ["PAGE001: pages may not use System.IO.File.ReadAllText (line 12)."];

        Assert.That(diagnostics, Is.EqualTo(expected));
    }

    [Test]
    public void ATypeReferenceIsNamedWithoutAMember() {
        IReadOnlyList<string> diagnostics = PageApiPolicy.Check([new ApiReference("System", "Type", string.Empty, 4)]);

        string[] expected = ["PAGE001: pages may not use System.Type (line 4)."];

        Assert.That(diagnostics, Is.EqualTo(expected));
    }

    [Test]
    public void RejectionWithoutALineOmitsTheLine() {
        IReadOnlyList<string> diagnostics = PageApiPolicy.Check([new ApiReference("System.IO", "File", "Exists", 0)]);

        string[] expected = ["PAGE001: pages may not use System.IO.File.Exists."];

        Assert.That(diagnostics, Is.EqualTo(expected));
    }

    [Test]
    public void TheSameApiIsReportedOnce() {
        IReadOnlyList<string> diagnostics = PageApiPolicy.Check([
            new ApiReference("System.IO", "File", "ReadAllText", 2),
            new ApiReference("System.IO", "File", "ReadAllText", 5),
        ]);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void DifferentApisAreReportedSeparately() {
        IReadOnlyList<string> diagnostics = PageApiPolicy.Check([
            new ApiReference("System.IO", "File", "ReadAllText", 2),
            new ApiReference("System", "Environment", "Exit", 3),
        ]);

        Assert.That(diagnostics, Has.Count.EqualTo(2));
    }

    [Test]
    public void AnEmptyPageIsAllowed() {
        Assert.That(PageApiPolicy.Check([]), Is.Empty);
    }
}
