// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Tests;

using System.Text.Json;
using uk.osric.asp.Core.Assistant;

[TestFixture]
public sealed class AccountBalanceJsonTests {

    [Test]
    public void ParsesTheProvidersBalanceForOneCurrency() {
        AccountBalance balance = AccountBalanceJson.Parse("""
            {
                "is_available": true,
                "balance_infos": [
                    {
                        "currency": "CNY",
                        "total_balance": "110.00",
                        "granted_balance": "10.00",
                        "topped_up_balance": "100.00"
                    }
                ]
            }
            """);

        Assert.Multiple(() => {
            Assert.That(balance.IsAvailable, Is.True);
            Assert.That(balance.Currencies, Has.Count.EqualTo(1));
            Assert.That(balance.Currencies[0], Is.EqualTo(new CurrencyBalance("CNY", "110.00", "10.00", "100.00")));
        });
    }

    [Test]
    public void KeepsEveryCurrencyTheProviderReportsInItsOwnOrder() {
        AccountBalance balance = AccountBalanceJson.Parse("""
            {
                "is_available": true,
                "balance_infos": [
                    { "currency": "USD", "total_balance": "5.00", "granted_balance": "0.00", "topped_up_balance": "5.00" },
                    { "currency": "CNY", "total_balance": "20.00", "granted_balance": "20.00", "topped_up_balance": "0.00" }
                ]
            }
            """);

        Assert.That(balance.Currencies.Select(currency => currency.Currency), Is.EqualTo(["USD", "CNY"]));
    }

    [Test]
    public void AnUnavailableBalanceIsNotAvailable() {
        AccountBalance balance = AccountBalanceJson.Parse("""
            { "is_available": false, "balance_infos": [] }
            """);

        Assert.That(balance.IsAvailable, Is.False);
    }

    [Test]
    public void AnEntryWithoutACurrencyIsSkippedRatherThanGuessedAt() {
        AccountBalance balance = AccountBalanceJson.Parse("""
            {
                "is_available": true,
                "balance_infos": [
                    { "total_balance": "1.00" },
                    { "currency": "USD", "total_balance": "2.00" }
                ]
            }
            """);

        Assert.That(balance.Currencies.Select(currency => currency.Currency), Is.EqualTo(["USD"]));
    }

    [Test]
    public void AMissingBalanceListIsAnEmptyBalance() {
        AccountBalance balance = AccountBalanceJson.Parse("""{ "is_available": true }""");

        Assert.That(balance.Currencies, Is.Empty);
    }

    [Test]
    public void ANonObjectBodyIsRefused() {
        Assert.Throws<FormatException>(() => AccountBalanceJson.Parse("[]"));
    }

    [Test]
    public void MalformedJsonIsRefused() {
        Assert.Catch<JsonException>(() => AccountBalanceJson.Parse("not json"));
    }
}
