// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using uk.osric.asp.Core.Assistant;

/// <summary>Samples the provider account's balance on a clock and hands it to the gauge.</summary>
/// <remarks>
/// The gauge reports the last sample, so a scrape never causes an outbound call and a slow provider
/// never holds one. A failed sample keeps the previous value; the reason is logged when it changes,
/// so a provider without a balance route costs one line rather than one per interval.
/// </remarks>
internal sealed class AccountBalanceSampler(
    IServiceScopeFactory scopes,
    AssistantMetrics metrics,
    AssistantOptions options,
    ILogger<AccountBalanceSampler> logger) : BackgroundService {

    private AccountBalanceReason? _lastReason;

    /// <summary>Reads one sample and records what it can read.</summary>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    public async Task SampleAsync(CancellationToken cancellationToken) {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IAccountBalanceClient client = scope.ServiceProvider.GetRequiredService<IAccountBalanceClient>();
        AccountBalanceRead read = await client.GetAsync(cancellationToken);

        LogReasonChange(read.Reason);
        if (read.Balance is not { } balance) {
            return;
        }

        List<CurrencyCredit> credits = [];
        foreach (CurrencyBalance currency in balance.Currencies) {
            if (AccountBalanceAmounts.TryRead(currency.Total, out decimal amount)) {
                credits.Add(new CurrencyCredit(currency.Currency, (double)amount));
            } else {
                // The figure is the provider's text, so only the currency code is logged: a zero on
                // the chart would read as credit spent rather than as a figure that could not be read.
                logger.LogWarning("The provider reported a {Currency} balance that is not a number, so it was not recorded",
                    currency.Currency);
            }
        }

        if (credits.Count > 0) {
            metrics.RecordBalance(credits);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        // A balance sample is an aside; an exception the client did not name must not stop the host,
        // so it is logged and the next tick tries again. A stopping host ends the loop quietly.
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(options.BalanceSampleSeconds));
        do {
            try {
                await SampleAsync(stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                return;
            } catch (Exception exception) {
                logger.LogError(exception, "The balance sample escaped its client; the last level is kept");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void LogReasonChange(AccountBalanceReason reason) {
        if (_lastReason == reason) {
            return;
        }

        bool recovered = _lastReason is not null && reason == AccountBalanceReason.Ok;
        _lastReason = reason;
        switch (reason) {
            case AccountBalanceReason.Ok when recovered:
                logger.LogInformation("The provider is answering the balance request again");
                break;
            case AccountBalanceReason.Ok:
                break;
            case AccountBalanceReason.Unsupported:
                logger.LogInformation("The provider has no balance route, so there is no account credit to show");
                break;
            case AccountBalanceReason.Timeout:
                logger.LogWarning("The provider did not answer the balance request in time");
                break;
            case AccountBalanceReason.Unreachable:
                logger.LogWarning("The provider could not be reached for the balance");
                break;
            case AccountBalanceReason.Invalid:
                logger.LogWarning("The provider returned an invalid balance response");
                break;
            case AccountBalanceReason.Error:
            default:
                logger.LogWarning("The provider answered the balance request with an error");
                break;
        }
    }
}
