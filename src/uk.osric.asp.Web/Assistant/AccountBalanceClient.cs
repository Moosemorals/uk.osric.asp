// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Net.Http.Headers;
using System.Text.Json;
using uk.osric.asp.Core.Assistant;
using uk.osric.asp.Web.Pages;

/// <summary>Reads the provider's DeepSeek-shaped <c>/user/balance</c> endpoint.</summary>
/// <remarks>
/// The balance panel is an aside on a page that has other work to do, so every failure — an error
/// status, a timeout, an unreachable host, a body that is not the balance JSON — is a null rather than
/// an exception, and the page says the provider did not report one. The provider's body is never
/// logged: it is a provider's text, and the status is the whole diagnosis.
/// </remarks>
internal sealed class AccountBalanceClient(
    HttpClient client,
    AssistantOptions options,
    ILogger<AccountBalanceClient> logger) : IAccountBalanceClient {

    private const string BalancePath = "/user/balance";

    /// <inheritdoc/>
    public async Task<AccountBalance?> GetAsync(CancellationToken cancellationToken) {
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
                logger.LogWarning("The provider answered {StatusCode} for the balance", (int)response.StatusCode);
                return null;
            }

            return AccountBalanceJson.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            logger.LogWarning("The provider did not answer the balance request in time");
            return null;
        } catch (HttpRequestException exception) {
            logger.LogWarning(exception, "The provider could not be reached for the balance");
            return null;
        } catch (Exception exception) when (exception is JsonException or FormatException) {
            logger.LogWarning(exception, "The provider returned an invalid balance response");
            return null;
        }
    }
}
