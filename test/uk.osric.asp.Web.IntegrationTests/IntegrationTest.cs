// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using uk.osric.asp.Core;
using uk.osric.asp.Web;

/// <summary>Starts the composed application once per fixture, against a throwaway database.</summary>
public abstract class IntegrationTest {

    private AspApplication _application = null!;

    /// <summary>The database the host owns, so a fixture can prove it never touched a real one.</summary>
    protected string DatabasePath => _application.DatabasePath;

    [OneTimeSetUp]
    public void StartApplication() {
        _application = new AspApplication();
    }

    [OneTimeTearDown]
    public void StopApplication() {
        _application.Dispose();
    }

    /// <summary>A client with its own cookie jar that does not follow redirects, so 302, 308 and the
    /// editor's flash cookie all stay visible to the test.</summary>
    protected HttpClient CreateClient() {
        return _application.CreateClient(new WebApplicationFactoryClientOptions {
            // Production is HTTPS behind Caddy, and the editor's flash cookie is Secure in
            // production, so the client has to speak HTTPS for the cookie to come back.
            BaseAddress = new Uri("https://localhost/"),
            AllowAutoRedirect = false,
        });
    }

    /// <summary>Stores a page without a compile result, as a direct write to the database would.</summary>
    protected async Task SavePageAsync(string path, string content) {
        await using AsyncServiceScope scope = _application.Services.CreateAsyncScope();
        IStorage storage = scope.ServiceProvider.GetRequiredService<IStorage>();
        await storage.SavePageAsync(new StoredPage(PagePath.Normalize(path), content));
    }

    /// <summary>How many times the running host has compiled a page.</summary>
    protected int CompileCount => _application.Compiler.CompileCount;
}
