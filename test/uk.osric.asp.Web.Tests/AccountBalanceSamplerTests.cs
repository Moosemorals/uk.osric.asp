// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Assistant;

[TestFixture]
[Category("Slow")]
[NonParallelizable]
public sealed class AccountBalanceSamplerTests {

    [Test]
    public async Task ASampleRecordsOneMeasurementPerReadableCurrencyAsync() {
        StubAccountBalanceClient client = new() {
            Read = AccountBalanceRead.Ok(Balance(("CNY", "110.00"), ("USD", "5.50"))),
        };
        using ServiceProvider services = Services(client);
        using AssistantMetrics metrics = new();
        RecordingLoggerFactory logs = new();
        using MeterListener listener = Listen(out List<(double Value, string Tags)> measurements);
        using AccountBalanceSampler sampler = Create(services, metrics, logs);

        await sampler.SampleAsync(CancellationToken.None);
        listener.RecordObservableInstruments();

        Assert.Multiple(() => {
            Assert.That(measurements, Is.EquivalentTo([(110.00, "currency=CNY"), (5.50, "currency=USD")]));
            Assert.That(logs.Entries, Is.Empty, "a provider that answers costs no log line");
        });
    }

    [Test]
    public async Task AFailedSampleKeepsTheLastValueAsync() {
        StubAccountBalanceClient client = new() {
            Read = AccountBalanceRead.Failed(AccountBalanceReason.Unreachable),
        };
        client.Reads.Enqueue(AccountBalanceRead.Ok(Balance(("CNY", "110.00"))));
        using ServiceProvider services = Services(client);
        using AssistantMetrics metrics = new();
        using MeterListener listener = Listen(out List<(double Value, string Tags)> measurements);
        using AccountBalanceSampler sampler = Create(services, metrics, new RecordingLoggerFactory());

        await sampler.SampleAsync(CancellationToken.None);
        measurements.Clear();
        await sampler.SampleAsync(CancellationToken.None);
        listener.RecordObservableInstruments();

        Assert.That(measurements, Is.EqualTo([(110.00, "currency=CNY")]));
    }

    [Test]
    public async Task AChangedReasonIsLoggedOnceAndARepeatedOneIsNotAsync() {
        StubAccountBalanceClient client = new() {
            Read = AccountBalanceRead.Failed(AccountBalanceReason.Unreachable),
        };
        client.Reads.Enqueue(AccountBalanceRead.Failed(AccountBalanceReason.Unsupported));
        client.Reads.Enqueue(AccountBalanceRead.Failed(AccountBalanceReason.Unsupported));
        using ServiceProvider services = Services(client);
        using AssistantMetrics metrics = new();
        RecordingLoggerFactory logs = new();
        using AccountBalanceSampler sampler = Create(services, metrics, logs);

        await sampler.SampleAsync(CancellationToken.None);
        await sampler.SampleAsync(CancellationToken.None);
        await sampler.SampleAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(logs.Entries, Has.Count.EqualTo(2), "the repeat is quiet");
            Assert.That(logs.Entries[0].Level, Is.EqualTo(LogLevel.Information), "no balance route is a fact, not a fault");
            Assert.That(logs.Entries[1].Level, Is.EqualTo(LogLevel.Warning));
        });
    }

    [Test]
    public async Task AnUnreadableFigureIsSkippedAndTheLogNamesOnlyItsCurrencyAsync() {
        StubAccountBalanceClient client = new() {
            Read = AccountBalanceRead.Ok(Balance(("CNY", "abc"), ("USD", "5.00"))),
        };
        using ServiceProvider services = Services(client);
        using AssistantMetrics metrics = new();
        RecordingLoggerFactory logs = new();
        using MeterListener listener = Listen(out List<(double Value, string Tags)> measurements);
        using AccountBalanceSampler sampler = Create(services, metrics, logs);

        await sampler.SampleAsync(CancellationToken.None);
        listener.RecordObservableInstruments();

        Assert.Multiple(() => {
            Assert.That(measurements, Is.EqualTo([(5.00, "currency=USD")]));
            Assert.That(logs.Entries, Has.Count.EqualTo(1));
            Assert.That(logs.Entries[0].Message, Does.Contain("CNY"));
            Assert.That(logs.Entries[0].Message, Does.Not.Contain("abc"), "the provider's text is never logged");
        });
    }

    private static AccountBalanceSampler Create(
        IServiceProvider services, AssistantMetrics metrics, RecordingLoggerFactory logs) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), metrics, TestOptions.Assistant,
            logs.CreateLogger<AccountBalanceSampler>());

    private static ServiceProvider Services(IAccountBalanceClient client) {
        ServiceCollection services = new();
        services.AddSingleton(client);
        return services.BuildServiceProvider();
    }

    private static AccountBalance Balance(params (string Currency, string Total)[] currencies) =>
        new(true, [.. currencies.Select(currency =>
            new CurrencyBalance(currency.Currency, currency.Total, "0.00", "0.00"))]);

    private static MeterListener Listen(out List<(double Value, string Tags)> measurements) {
        List<(double Value, string Tags)> values = [];
        measurements = values;
        MeterListener listener = new();
        listener.InstrumentPublished = (instrument, current) => {
            if (instrument.Meter.Name == AssistantMetrics.MeterName) {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => {
            if (instrument.Name == "assistant.balance") {
                values.Add((value, string.Join(',', tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}"))));
            }
        });
        listener.Start();
        return listener;
    }
}
