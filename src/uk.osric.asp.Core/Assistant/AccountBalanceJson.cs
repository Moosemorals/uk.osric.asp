// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Assistant;

using System.Text.Json;

/// <summary>Reads the provider's balance response without depending on anything but the text.</summary>
/// <remarks>
/// The figures stay the provider's own strings: they are money it already formatted, and rounding or
/// reformatting them would put a second opinion between the reader and the account. A malformed entry
/// is skipped rather than guessed at, so one bad line does not cost the rest of the response.
/// </remarks>
public static class AccountBalanceJson {

    /// <summary>Parses the JSON body the provider's balance endpoint returns.</summary>
    /// <param name="json">The response body.</param>
    /// <returns>The reported balance, which may name no currencies.</returns>
    /// <exception cref="JsonException">The body is not valid JSON.</exception>
    /// <exception cref="FormatException">The body is valid JSON but not an object.</exception>
    public static AccountBalance Parse(string json) {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) {
            throw new FormatException("The balance response must be a JSON object.");
        }

        bool available = root.TryGetProperty("is_available", out JsonElement flag)
            && flag.ValueKind == JsonValueKind.True;

        List<CurrencyBalance> currencies = [];
        if (root.TryGetProperty("balance_infos", out JsonElement infos) && infos.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement info in infos.EnumerateArray()) {
                if (info.ValueKind != JsonValueKind.Object) {
                    continue;
                }
                string? currency = Text(info, "currency");
                if (string.IsNullOrWhiteSpace(currency)) {
                    continue;
                }
                currencies.Add(new CurrencyBalance(
                    currency,
                    Text(info, "total_balance") ?? string.Empty,
                    Text(info, "granted_balance") ?? string.Empty,
                    Text(info, "topped_up_balance") ?? string.Empty));
            }
        }

        return new AccountBalance(available, currencies);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
