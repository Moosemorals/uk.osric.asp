// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Pages;

/// <summary>Reads the provider's DeepSeek-shaped <c>/user/balance</c> endpoint.</summary>
/// <remarks>
/// The balance is an aside on a page that has other work to do, so every failure — an error status, a
/// timeout, an unreachable host, a body that is not the balance JSON — is a reason rather than an
/// exception. The caller decides what the reason is worth: the page says the provider did not report a
/// balance, and the sampler logs a change. The provider's body is never returned or logged: it is a
/// provider's text, and the status is the whole diagnosis.
/// </remarks>
internal sealed class AccountBalanceClient(
    HttpClient client,
    AssistantOptions options) : IAccountBalanceClient {

    private const string BalancePath = "/user/balance";

    /// <inheritdoc/>
    public async Task<AccountBalanceRead> GetAsync(CancellationToken cancellationToken) {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.BalanceTimeoutSeconds));

        Uri endpoint = new(options.BaseUrl.TrimEnd('/') + BalancePath);
        using HttpRequestMessage message = new(HttpMethod.Get, endpoint);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.TryAddWithoutValidation("User-Agent", OutboundUserAgent.For(OutboundOrigin.Assistant));
        if (options.RequireApiKey && !string.IsNullOrWhiteSpace(options.ApiKey)) {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        try {
            using HttpResponseMessage response = await client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) {
                return AccountBalanceRead.Failed(response.StatusCode == HttpStatusCode.NotFound
                    ? AccountBalanceReason.Unsupported
                    : AccountBalanceReason.Error);
            }

            AccountBalance balance = AccountBalanceJson.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return AccountBalanceRead.Ok(balance);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return AccountBalanceRead.Failed(AccountBalanceReason.Timeout);
        } catch (HttpRequestException) {
            return AccountBalanceRead.Failed(AccountBalanceReason.Unreachable);
        } catch (Exception exception) when (exception is JsonException or FormatException) {
            return AccountBalanceRead.Failed(AccountBalanceReason.Invalid);
        }
    }
}
