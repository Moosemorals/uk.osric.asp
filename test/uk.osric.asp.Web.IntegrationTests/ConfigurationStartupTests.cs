// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.IntegrationTests;

/// <summary>Proves a host cannot come up with a configuration it could not run.</summary>
/// <remarks>
/// The report goes to standard error and the entry point exits, so the test host sees a host that was
/// never built. That is the production behaviour the operator meets, so the text is what is asserted.
/// </remarks>
[TestFixture]
public sealed class ConfigurationStartupTests {

    [Test]
    public void AnOutOfRangeValueStopsTheHostBeforeItServes() {
        using AspApplication application = new(renderTimeoutSeconds: 0);
        using StringWriter captured = new();
        TextWriter original = Console.Error;

        try {
            Console.SetError(captured);
            _ = Assert.Throws<InvalidOperationException>(() => application.CreateClient());
        } finally {
            Console.SetError(original);
        }

        string report = captured.ToString();
        Assert.Multiple(() => {
            Assert.That(report, Does.Contain("Pages:Render:TimeoutSeconds is 0"));
            Assert.That(report, Does.Contain("Set these values in appsettings.json"));
        });
    }
}
