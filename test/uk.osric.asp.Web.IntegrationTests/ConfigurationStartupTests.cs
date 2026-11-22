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
        string report = ReportFor(new Dictionary<string, string?> {
            ["Pages:Render:TimeoutSeconds"] = "0",
        });

        Assert.Multiple(() => {
            Assert.That(report, Does.Contain("Pages:Render:TimeoutSeconds is 0"));
            Assert.That(report, Does.Contain("Set these values in appsettings.json"));
        });
    }

    [Test]
    public void EveryBadSectionIsReportedInOneStart() {
        string report = ReportFor(new Dictionary<string, string?> {
            ["Pages:Render:Slots"] = "0",
            ["Pages:Read:MaxRowsPerList"] = "not a number",
            ["Metrics:TimeZone"] = "Mars/Olympus",
            ["Authentication:Login:PermitLimit"] = "0",
        });

        Assert.Multiple(() => {
            Assert.That(report, Does.Contain("Pages:Render:Slots is 0"));
            Assert.That(report, Does.Contain("Pages:Read:MaxRowsPerList is \"not a number\""));
            Assert.That(report, Does.Contain("Metrics:TimeZone is \"Mars/Olympus\""));
            Assert.That(report, Does.Contain("Authentication:Login:PermitLimit is 0"));
        });
    }

    // The reserved keys are the shell's route table now. A deployment that kept the old section
    // would believe it still narrowed or widened something, so it is refused rather than ignored.
    [Test]
    public void AReservedPathsSectionFromAnEarlierReleaseStopsTheHost() {
        string report = ReportFor(new Dictionary<string, string?> {
            ["Pages:ReservedPaths:0"] = "/admin",
        });

        Assert.Multiple(() => {
            Assert.That(report, Does.Contain("Pages:ReservedPaths is no longer read"));
            Assert.That(report, Does.Contain("the reserved list"));
        });
    }

    // The two value sections of the release before this one are folded into Pages:Stores, so a
    // deployment that kept them would believe it still bounded a page's or a session's values.
    [Test]
    public void AStorageSectionFromAnEarlierReleaseStopsTheHost() {
        string report = ReportFor(new Dictionary<string, string?> {
            ["Pages:Storage:MaxPageBytes"] = "1024",
        });

        Assert.Multiple(() => {
            Assert.That(report, Does.Contain("Pages:Storage is no longer read"));
            Assert.That(report, Does.Contain("Pages:Stores"));
        });
    }

    [Test]
    public void ASessionSectionFromAnEarlierReleaseStopsTheHost() {
        string report = ReportFor(new Dictionary<string, string?> {
            ["Pages:Session:IdleMinutes"] = "5",
        });

        Assert.Multiple(() => {
            Assert.That(report, Does.Contain("Pages:Session is no longer read"));
            Assert.That(report, Does.Contain("Pages:Stores:Visitor"));
        });
    }

    [Test]
    public void AMissingProviderKeyStopsTheHostAndNamesIt() {
        string report = ReportFor(new Dictionary<string, string?> {
            ["Assistant:ApiKey"] = null,
        });

        Assert.Multiple(() => {
            Assert.That(report, Does.Contain("Assistant:ApiKey is not set."));
            Assert.That(report, Does.Not.Contain("ApiKey="));
        });
    }

    private static string ReportFor(Dictionary<string, string?> settings) {
        using AspApplication application = new(settings: settings);
        using StringWriter captured = new();
        TextWriter original = Console.Error;

        try {
            Console.SetError(captured);
            _ = Assert.Throws<InvalidOperationException>(() => application.CreateClient());
        } finally {
            Console.SetError(original);
        }

        return captured.ToString();
    }
}
