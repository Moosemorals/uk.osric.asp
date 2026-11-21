// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using uk.osric.asp.Web.Documentation;

/// <summary>The two reference pages, through the endpoint that writes them.</summary>
[TestFixture]
public sealed class ReferenceEndpointTests {

    private static ReferenceLibrary Library() => TestReference.Library;

    [Test]
    public async Task TheIndexListsEveryAssemblyThisBuildCarries() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/reference");

        await ReferenceEndpoint.IndexAsync(context, Library(), CancellationToken.None);
        string body = await TestHttp.ReadBodyAsync(context);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(body, Does.Contain("href=\"/admin/reference/sdk\""));
            Assert.That(body, Does.Contain("The page contract"));
            Assert.That(body, Does.Contain("The site"));
            Assert.That(body, Does.Contain("What this page shows"));
        });
    }

    [Test]
    public async Task TheIndexSaysWhenTheBuildCarriesNothing() {
        string directory = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.reference.{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);

        try {
            DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/reference");
            await ReferenceEndpoint.IndexAsync(context, new ReferenceLibrary(directory, NullLogger<ReferenceLibrary>.Instance),
                CancellationToken.None);
            string body = await TestHttp.ReadBodyAsync(context);

            Assert.Multiple(() => {
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(body, Does.Contain("carries no documentation"));
            });
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task AnAssemblyPageNamesItsTypesAndMembers() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/reference/sdk");

        await ReferenceEndpoint.AssemblyAsync(context, "sdk", Library(), CancellationToken.None);
        string body = await TestHttp.ReadBodyAsync(context);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(body, Does.Contain("The page contract"));
            Assert.That(body, Does.Contain("id=\"t-IContext\""));
            Assert.That(body, Does.Contain("id=\"m-IContext.Request\""));
            Assert.That(body, Does.Contain("Site administration"), "the admin menu");
            Assert.That(body, Does.Contain("<span class=\"current\" aria-current=\"true\">Reference</span>"));
        });
    }

    [Test]
    public async Task TheIndexCountsOneTypeAndItsMembersInWords() {
        string directory = Path.Combine(Path.GetTempPath(), $"uk.osric.asp.reference.{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);

        try {
            await File.WriteAllTextAsync(Path.Combine(directory, "reference.sdk.json"), """
                {
                  "slug": "sdk",
                  "assembly": "Test.Assembly",
                  "title": "The test",
                  "description": "For tests.",
                  "typeCount": 1,
                  "entryCount": 2,
                  "namespaces": []
                }
                """);

            DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/reference");
            await ReferenceEndpoint.IndexAsync(context, new ReferenceLibrary(directory, NullLogger<ReferenceLibrary>.Instance),
                CancellationToken.None);
            string body = await TestHttp.ReadBodyAsync(context);

            Assert.That(body, Does.Contain("1 type, 2 entries"), "a count of one takes no s, and entry does not take one");
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task OneNamespaceIsItsOwnPage() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/reference/core/uk.osric.asp.Core.Assistant");

        await ReferenceEndpoint.NamespaceAsync(
            context, "core", "uk.osric.asp.Core.Assistant", Library(), CancellationToken.None);
        string body = await TestHttp.ReadBodyAsync(context);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(body, Does.Contain("uk.osric.asp.Core.Assistant"));
            Assert.That(body, Does.Contain("<details class=\"type\""));
        });
    }

    [Test]
    public async Task AnAssemblyWithSeveralNamespacesListsThem() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/reference/core");

        await ReferenceEndpoint.AssemblyAsync(context, "core", Library(), CancellationToken.None);
        string body = await TestHttp.ReadBodyAsync(context);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(body, Does.Contain("href=\"/admin/reference/core/uk.osric.asp.Core.Assistant\""));
            Assert.That(body, Does.Contain("Namespaces"));
        });
    }

    [Test]
    public async Task AnUnknownSlugIsANotFoundNamingIt() {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin/reference/nope");

        await ReferenceEndpoint.AssemblyAsync(context, "nope", Library(), CancellationToken.None);
        string body = await TestHttp.ReadBodyAsync(context);

        Assert.Multiple(() => {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(body, Does.Contain("nope"));
        });
    }

    [Test]
    public async Task BothPagesLinkTheSeedSheetAndAreNoStore() {
        DefaultHttpContext index = TestHttp.CreateContext(null, "GET", "/admin/reference");
        await ReferenceEndpoint.IndexAsync(index, Library(), CancellationToken.None);
        string indexBody = await TestHttp.ReadBodyAsync(index);

        DefaultHttpContext assembly = TestHttp.CreateContext(null, "GET", "/admin/reference/sdk");
        await ReferenceEndpoint.AssemblyAsync(assembly, "sdk", Library(), CancellationToken.None);
        string assemblyBody = await TestHttp.ReadBodyAsync(assembly);

        Assert.Multiple(() => {
            Assert.That(indexBody, Does.Contain("/reference.css"));
            Assert.That(assemblyBody, Does.Contain("/reference.css"));
            Assert.That(index.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(assembly.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(index.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
            Assert.That(assembly.Response.ContentType, Is.EqualTo("text/html; charset=utf-8"));
        });
    }
}
