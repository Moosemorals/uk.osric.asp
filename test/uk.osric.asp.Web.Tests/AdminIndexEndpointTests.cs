// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using uk.osric.asp.Web.Admin;

[TestFixture]
public sealed class AdminIndexEndpointTests {

    [Test]
    public async Task TheOwnerIsOfferedEveryToolTheShellHas() {
        string body = await RenderAsync(signedIn: true);

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain(">Import pages</a>"));
            Assert.That(body, Does.Contain(">Export pages</a>"));
            Assert.That(body, Does.Contain(">Seeds</a>"));
            Assert.That(body, Does.Contain(">Status</a>"));
            Assert.That(body, Does.Contain(">Page list</a>"));
            Assert.That(body, Does.Not.Contain("The rest of the administration is for the owner"));
        });
    }

    // A public index was the owner's choice, and hiding a tool is the audience's job rather than the
    // route's: a visitor is never sent to the sign-in page to find out what is there.
    [Test]
    public async Task AVIsitorIsOfferedThePublicToolsAndToldTheRestNeedASignIn() {
        string body = await RenderAsync(signedIn: false);

        Assert.Multiple(() => {
            Assert.That(body, Does.Contain(">Reference</a>"));
            Assert.That(body, Does.Contain(">Help</a>"));
            Assert.That(body, Does.Not.Contain(">Import pages</a>"));
            Assert.That(body, Does.Not.Contain(">Status</a>"));
            Assert.That(body, Does.Not.Contain(">Seeds</a>"));
            Assert.That(body, Does.Contain("The rest of the administration is for the owner"));
        });
    }

    [Test]
    public async Task TheIndexDoesNotOfferItself() {
        string body = await RenderAsync(signedIn: true);

        Assert.That(body, Does.Not.Contain("href=\"/admin\""));
    }

    private static async Task<string> RenderAsync(bool signedIn) {
        DefaultHttpContext context = TestHttp.CreateContext(null, "GET", "/admin");
        if (signedIn) {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "owner")], "test"));
        }

        await AdminIndexEndpoint.HandleAsync(context, CancellationToken.None);

        return await TestHttp.ReadBodyAsync(context);
    }
}
