// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net;
using System.Text.RegularExpressions;
using uk.osric.asp.Core;
using uk.osric.asp.Web;
using uk.osric.asp.Web.Users;

/// <summary>Starts the composed application once per fixture, against a throwaway database.</summary>
public abstract class IntegrationTest {

    /// <summary>The account every fixture creates.</summary>
    protected const string UserName = "owner";

    /// <summary>The password for <see cref="UserName"/>.</summary>
    protected const string Password = "correct horse battery staple";

    private AspApplication _application = null!;

    /// <summary>The database the host owns, so a fixture can prove it never touched a real one.</summary>
    protected string DatabasePath => _application.DatabasePath;

    [OneTimeSetUp]
    public async Task StartApplicationAsync() {
        _application = new AspApplication();
        await SeedUserAsync();
    }

    [OneTimeTearDown]
    public void StopApplication() {
        _application.Dispose();
    }

    /// <summary>A client with its own cookie jar that does not follow redirects, so 302, 303, 308 and
    /// the auth, antiforgery and session cookies all stay visible to the test.</summary>
    protected HttpClient CreateClient() {
        return _application.CreateClient(new WebApplicationFactoryClientOptions {
            // Production is HTTPS behind Caddy, and the cookies are Secure in production, so the
            // client has to speak HTTPS for them to come back.
            BaseAddress = new Uri("https://localhost/"),
            AllowAutoRedirect = false,
        });
    }

    /// <summary>A client that has signed in through the real login form.</summary>
    protected async Task<HttpClient> CreateSignedInClientAsync(string? userName = null, string? password = null) {
        HttpClient client = CreateClient();
        await SignInAsync(client, userName, password);
        return client;
    }

    /// <summary>Signs a client in through <c>POST /login</c>, antiforgery token and all.</summary>
    protected async Task SignInAsync(HttpClient client, string? userName = null, string? password = null) {
        string token = await GetAntiforgeryTokenAsync(client, "/login");

        HttpResponseMessage response = await client.PostAsync("/login", Form(
            ("username", userName ?? UserName),
            ("password", password ?? Password),
            ("rememberMe", "on"),
            ("__RequestVerificationToken", token)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.SeeOther));
    }

    /// <summary>Reads the antiforgery token a form rendered.</summary>
    protected static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path) {
        HttpResponseMessage response = await client.GetAsync(path);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"{path} answered {(int)response.StatusCode}");

        string body = await response.Content.ReadAsStringAsync();
        Match match = Regex.Match(body, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
        Assert.That(match.Success, Is.True, $"{path} did not render an antiforgery token");

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>Saves a page through the editor, with a fresh token.</summary>
    protected async Task<HttpResponseMessage> PostEditorAsync(HttpClient client, string path, string? content) {
        string token = await GetAntiforgeryTokenAsync(client, "/edit/" + path);
        Dictionary<string, string> fields = new() { ["__RequestVerificationToken"] = token };

        if (content is not null) {
            fields["content"] = content;
        }

        return await client.PostAsync("/edit/" + path, new FormUrlEncodedContent(fields));
    }

    /// <summary>Stores a page without a compile result, as a direct write to the database would.</summary>
    protected async Task SavePageAsync(string path, string content) {
        await using AsyncServiceScope scope = _application.Services.CreateAsyncScope();
        IStorage storage = scope.ServiceProvider.GetRequiredService<IStorage>();
        await storage.SavePageAsync(new StoredPage(PagePath.Normalize(path), content));
    }

    /// <summary>Reads a page straight from storage, bypassing routing and the editor.</summary>
    protected async Task<StoredPage?> ReadPageAsync(string path) {
        await using AsyncServiceScope scope = _application.Services.CreateAsyncScope();
        IStorage storage = scope.ServiceProvider.GetRequiredService<IStorage>();
        return await storage.GetPageAsync(PagePath.Normalize(path));
    }

    /// <summary>Adds an account beyond the one every fixture starts with.</summary>
    protected async Task SeedAdditionalUserAsync(string userName, string password) {
        await using AsyncServiceScope scope = _application.Services.CreateAsyncScope();
        IUserStore users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        IPasswordService passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();

        LocalUser template = new() {
            UserName = "~template~",
            PasswordHash = string.Empty,
            SecurityStamp = SecurityStamp.Create(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _ = await users.TryAddAsync(new LocalUser {
            UserName = userName,
            PasswordHash = passwords.Hash(template, password),
            SecurityStamp = template.SecurityStamp,
            CreatedAt = template.CreatedAt,
        });
    }

    /// <summary>Changes a password the way the CLI does, rotating the security stamp.</summary>
    protected async Task ChangePasswordAsync(string userName, string password) {
        await using AsyncServiceScope scope = _application.Services.CreateAsyncScope();
        IUserStore users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        IPasswordService passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();

        LocalUser? user = await users.FindAsync(userName);
        Assert.That(user, Is.Not.Null);

        Assert.That(
            await users.ChangePasswordAsync(userName, passwords.Hash(user, password), SecurityStamp.Create()),
            Is.True);
    }

    /// <summary>Removes an account, the way the CLI does.</summary>
    protected async Task RemoveUserAsync(string userName) {
        await using AsyncServiceScope scope = _application.Services.CreateAsyncScope();
        IUserStore users = scope.ServiceProvider.GetRequiredService<IUserStore>();

        Assert.That(await users.RemoveAsync(userName), Is.True);
    }

    /// <summary>How many times the running host has compiled a page.</summary>
    protected int CompileCount => _application.Compiler.CompileCount;

    /// <summary>Builds a form body from named values.</summary>
    protected static FormUrlEncodedContent Form(params (string Name, string Value)[] fields) {
        return new FormUrlEncodedContent(fields.ToDictionary(field => field.Name, field => field.Value));
    }

    private async Task SeedUserAsync() {
        await using AsyncServiceScope scope = _application.Services.CreateAsyncScope();
        IUserStore users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        IPasswordService passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        string name = UserName;

        LocalUser template = new() {
            UserName = "~template~",
            PasswordHash = string.Empty,
            SecurityStamp = SecurityStamp.Create(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        LocalUser user = new() {
            UserName = name,
            PasswordHash = passwords.Hash(template, Password),
            SecurityStamp = template.SecurityStamp,
            CreatedAt = template.CreatedAt,
        };

        Assert.That(await users.TryAddAsync(user), Is.True);
    }
}
