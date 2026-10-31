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
        ApiReference reference = new(ns, type, member, new TemplatePosition(3, 1));

        IReadOnlyList<PageDiagnostic> diagnostics = PageApiPolicy.Check([reference]);

        Assert.That(diagnostics, allowed ? Is.Empty : Is.Not.Empty);
    }

    [Test]
    public void RejectionNamesTheApiAndThePageLine() {
        IReadOnlyList<PageDiagnostic> diagnostics = PageApiPolicy.Check([
            new ApiReference("System.IO", "File", "ReadAllText", new TemplatePosition(12, 5)),
        ]);

        Assert.That(diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(12, 5),
                "PAGE001: pages may not use System.IO.File.ReadAllText."),
        }));
    }

    [Test]
    public void ATypeReferenceIsNamedWithoutAMember() {
        IReadOnlyList<PageDiagnostic> diagnostics = PageApiPolicy.Check([
            new ApiReference("System", "Type", string.Empty, new TemplatePosition(4, 2)),
        ]);

        Assert.That(diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, new TemplatePosition(4, 2),
                "PAGE001: pages may not use System.Type."),
        }));
    }

    [Test]
    public void RejectionWithoutALineOmitsTheLine() {
        IReadOnlyList<PageDiagnostic> diagnostics = PageApiPolicy.Check([
            new ApiReference("System.IO", "File", "Exists", null),
        ]);

        Assert.That(diagnostics, Is.EqualTo(new PageDiagnostic[] {
            new(PageDiagnosticSeverity.Error, null, "PAGE001: pages may not use System.IO.File.Exists."),
        }));
    }

    [Test]
    public void TheSameApiIsReportedOnce() {
        IReadOnlyList<PageDiagnostic> diagnostics = PageApiPolicy.Check([
            new ApiReference("System.IO", "File", "ReadAllText", new TemplatePosition(2, 1)),
            new ApiReference("System.IO", "File", "ReadAllText", new TemplatePosition(5, 1)),
        ]);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void DifferentApisAreReportedSeparately() {
        IReadOnlyList<PageDiagnostic> diagnostics = PageApiPolicy.Check([
            new ApiReference("System.IO", "File", "ReadAllText", new TemplatePosition(2, 1)),
            new ApiReference("System", "Environment", "Exit", new TemplatePosition(3, 1)),
        ]);

        Assert.That(diagnostics, Has.Count.EqualTo(2));
    }

    [Test]
    public void AnEmptyPageIsAllowed() {
        Assert.That(PageApiPolicy.Check([]), Is.Empty);
    }
}
