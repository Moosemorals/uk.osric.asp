// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Sdk;

/// <summary>The languages a check answers for.</summary>
/// <remarks>
/// <para>
/// The set is closed on purpose: a check names one of these, and the host refuses a name that is not
/// one of them rather than guessing from a key's extension. A key's extension is a content type, not
/// a validator: a <c>.html</c> page can hold a template or an asset.
/// </para>
/// <para>
/// One declaration feeds the author manual, the assistant's tool schema and the refusal, so a caller
/// never reads a second list. The names a caller passes are <see cref="PageCheckLanguages.Names"/>.
/// </para>
/// </remarks>
public enum PageCheckLanguage {

    /// <summary>A page template: generation, the page API policy and the compiler.</summary>
    Template,
}
