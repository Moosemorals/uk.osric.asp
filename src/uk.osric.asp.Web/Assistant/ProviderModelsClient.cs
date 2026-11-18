// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using uk.osric.asp.Web.Pages;

/// <summary>Reads the provider's model list, which is where a model's limits are written down.</summary>
/// <remarks>
/// Every failure — an error status, a timeout, an unreachable host, a body that is not the model
/// list — is a reason rather than an exception, so a provider that is down at startup costs the
/// assistant its budget and nothing else. The provider's body is never logged: the reason is the
/// whole diagnosis, and the model list is not this host's to publish.
/// </remarks>
internal sealed class ProviderModelsClient(
    HttpClient client,
    AssistantOptions options) : IProviderModelsClient {

    /// <inheritdoc/>
    public async Task<ModelWindowRead> GetAsync(CancellationToken cancellationToken) {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ModelsTimeoutSeconds));

        Uri endpoint = new(options.BaseUrl.TrimEnd('/') + options.ModelsPath);
        using HttpRequestMessage message = new(HttpMethod.Get, endpoint);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.TryAddWithoutValidation("User-Agent", OutboundOrigin.Assistant.UserAgent());
        if (options.RequireApiKey && !string.IsNullOrWhiteSpace(options.ApiKey)) {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        try {
            using HttpResponseMessage response = await client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) {
                return ModelWindowRead.Failed(ModelWindowReason.Error);
            }

            return Parse(await response.Content.ReadAsStringAsync(timeout.Token), options.Model);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return ModelWindowRead.Failed(ModelWindowReason.Timeout);
        } catch (HttpRequestException) {
            return ModelWindowRead.Failed(ModelWindowReason.Unreachable);
        } catch (Exception exception) when (exception is JsonException or FormatException) {
            return ModelWindowRead.Failed(ModelWindowReason.Invalid);
        }
    }

    /// <summary>Finds the configured model's figures in the provider's answer.</summary>
    /// <param name="body">The JSON the provider answered with.</param>
    /// <param name="model">The configured model name.</param>
    /// <returns>The window and output limit, or the reason there is none.</returns>
    /// <remarks>
    /// The provider says either figure "may be omitted when the information is not available for that
    /// model", and guessing one is what the budget's decision refuses, so an omitted figure is a
    /// missing window rather than a default. A model absent from the list is a different answer: the
    /// provider is authoritative, and the owner hears about it at startup.
    /// </remarks>
    internal static ModelWindowRead Parse(string body, string model) {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);

        using JsonDocument document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.Array) {
            return ModelWindowRead.Failed(ModelWindowReason.Invalid);
        }

        foreach (JsonElement entry in data.EnumerateArray()) {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String
                || !string.Equals(id.GetString(), model, StringComparison.Ordinal)) {
                continue;
            }

            if (!entry.TryGetProperty("context_window", out JsonElement window)
                || !window.TryGetInt64(out long contextWindow)
                || !entry.TryGetProperty("max_output_tokens", out JsonElement output)
                || !output.TryGetInt32(out int maximumOutput)) {
                return ModelWindowRead.Failed(ModelWindowReason.Incomplete);
            }

            return ModelWindowRead.Ok(contextWindow, maximumOutput);
        }

        return ModelWindowRead.Failed(ModelWindowReason.ModelMissing);
    }
}
