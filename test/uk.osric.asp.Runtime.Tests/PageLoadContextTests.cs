// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using NUnit.Framework;
using System.Reflection;
using uk.osric.asp.Core;
using uk.osric.asp.Runtime;

[TestFixture]
public sealed class PageLoadContextTests {

    [TestCase("uk.osric.asp.Web")]
    [TestCase("uk.osric.asp.Web.Endpoints")]
    [TestCase("uk.osric.asp.Runtime")]
    [TestCase("Microsoft.AspNetCore.Http")]
    [TestCase("Microsoft.EntityFrameworkCore")]
    [TestCase("Microsoft.Data.Sqlite")]
    public void ShellAssembliesAreRefused(string assembly) {
        PageLoadContext context = new();

        FileLoadException? failure = Assert.Throws<FileLoadException>(
            () => context.LoadFromAssemblyName(new AssemblyName(assembly)));

        // A missing assembly is a FileNotFoundException; a refused one is a FileLoadException that
        // carries the refusal, so this proves the context said no rather than "not here".
        Assert.That(failure?.InnerException?.Message, Does.Contain("may not load"));
    }

    [Test]
    public void TheSdkResolvesThroughTheDefaultContext() {
        PageLoadContext context = new();

        Assembly resolved = context.LoadFromAssemblyName(typeof(IHandler).Assembly.GetName());

        Assert.That(resolved, Is.SameAs(typeof(IHandler).Assembly));
    }

    [Test]
    public void PlatformAssembliesAreStillAvailable() {
        PageLoadContext context = new();

        Assembly resolved = context.LoadFromAssemblyName(typeof(string).Assembly.GetName());

        Assert.That(resolved, Is.SameAs(typeof(string).Assembly));
    }
}
