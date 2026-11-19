// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Assistant;

/// <summary>The model list read, as the sampler and the run ask for it.</summary>
internal interface IProviderModelsClient {

    /// <summary>Reads the configured model's window and output limit.</summary>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    /// <returns>What the provider answered, with the reason when it carries no figures.</returns>
    Task<ModelWindowRead> GetAsync(CancellationToken cancellationToken);
}
